#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    /// <summary>Render-only model and attack geometry. Never instantiates or runs enemy scripts.</summary>
    [InitializeOnLoad]
    public static class BaekhoEnemyForgePreview
    {
        const int ImageSize = 512;
        const double IdleSeconds = 30;
        static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>();
        static double nextSweep;

        static BaekhoEnemyForgePreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CloseAll;
            EditorApplication.quitting += CloseAll;
            EditorApplication.playModeStateChanged += _ => CloseAll();
            EditorApplication.update += Sweep;
        }

        public static object Handle(string action, JObject payload)
        {
            payload = payload ?? new JObject();
            if (action == "preview_close")
            {
                var id = (string)payload["sessionId"];
                if (id != null && Sessions.TryGetValue(id, out var previous))
                {
                    Sessions.Remove(id);
                    previous.Dispose();
                }
                return new { closed = true };
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("미리보기는 Unity의 Play Mode, 컴파일, 에셋 가져오기가 끝난 뒤 사용할 수 있습니다.");
            if (action == "preview_open")
            {
                if (Sessions.Count >= 4) throw new InvalidOperationException("열린 미리보기가 너무 많습니다. 이전 미리보기를 닫아 주세요.");
                var blueprint = payload["blueprint"] as JObject ?? payload;
                var session = new Session(blueprint);
                Sessions.Add(session.Id, session);
                return new { sessionId = session.Id };
            }
            if (action == "preview_frame")
            {
                var id = (string)payload["sessionId"];
                if (id == null || !Sessions.TryGetValue(id, out var session))
                    throw new InvalidOperationException("미리보기 세션이 만료되었습니다. 모델 미리보기를 다시 열어 주세요.");
                return session.Frame(payload);
            }
            throw new ArgumentException("지원하지 않는 미리보기 작업입니다: " + action);
        }

        public static void CloseAll()
        {
            foreach (var session in Sessions.Values.ToArray()) session.Dispose();
            Sessions.Clear();
        }

        static void Sweep()
        {
            if (EditorApplication.timeSinceStartup < nextSweep) return;
            nextSweep = EditorApplication.timeSinceStartup + 1;
            foreach (var pair in Sessions.Where(p => EditorApplication.timeSinceStartup - p.Value.LastUse > IdleSeconds).ToArray())
            {
                Sessions.Remove(pair.Key);
                pair.Value.Dispose();
            }
        }

        sealed class Session : IDisposable
        {
            public readonly string Id = Guid.NewGuid().ToString("D");
            public double LastUse { get; private set; }
            readonly PreviewRenderUtility preview;
            readonly GameObject root;
            readonly GameObject model;
            readonly Animator animator;
            readonly Mesh lines;
            readonly Material lineMaterial;
            readonly Texture2D pixels;
            readonly string directory;
            readonly string modelIdentity;
            readonly string materialWarning;
            readonly Dictionary<Transform, Pose> bindPose = new Dictionary<Transform, Pose>();
            readonly Queue<string> frameFiles = new Queue<string>();
            readonly Dictionary<string, JObject> descriptors = new Dictionary<string, JObject>();
            PlayableGraph graph;
            AnimationClipPlayable playable;
            AnimationClip currentClip;
            AnimatorController moveController;
            AnimatorControllerPlayable movePlayable;
            string currentMove;
            string clipWarning;
            long lastSequence = -1;
            double lastDescriptorScan;
            Bounds modelBounds;
            Bounds cameraBounds;
            string boundsIdentity;
            bool disposed;

            public Session(JObject blueprint)
            {
                LastUse = EditorApplication.timeSinceStartup;
                var reference = blueprint["model"];
                var source = BaekhoEnemyForgeCatalog.Resolve(reference, typeof(GameObject)) as GameObject;
                if (!source) throw new InvalidOperationException("미리보기 모델 프리팹 또는 FBX를 선택해 주세요.");
                source = ResolveRenderSource(blueprint, source);
                modelIdentity = Identity(reference);
                if (source.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => !m || !m.shader || !m.shader.isSupported)))
                    materialWarning = "모델에 누락되었거나 지원되지 않는 재질이 있습니다. Unity에서 현재 렌더 파이프라인에 맞는 재질을 연결한 모델 프리팹을 선택해 주세요.";
                directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "BaekhoEnemyForge", "Preview", Id);
                try
                {
                    preview = new PreviewRenderUtility();
                    root = HiddenObject("Enemy Forge render preview");
                    var map = new Dictionary<Transform, Transform>();
                    model = CloneTransforms(source.transform, root.transform, map).gameObject;
                    CopyRenderers(map);
                    var sourceAnimator = source.GetComponentInChildren<Animator>(true);
                    var animatorRoot = sourceAnimator && map.TryGetValue(sourceAnimator.transform, out var mapped) ? mapped.gameObject : model;
                    animator = animatorRoot.AddComponent<Animator>();
                    animator.avatar = sourceAnimator ? sourceAnimator.avatar : null;
                    animator.runtimeAnimatorController = null;
                    animator.applyRootMotion = false;
                    animator.fireEvents = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    foreach (var transform in model.GetComponentsInChildren<Transform>(true)) bindPose.Add(transform, new Pose(transform));
                    SetModelTransform(blueprint);
                    model.SetActive(true);

                    var shader = Shader.Find("Hidden/Internal-Colored");
                    if (!shader) throw new InvalidOperationException("미리보기 선을 렌더할 Unity 셰이더를 찾지 못했습니다.");
                    lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    lineMaterial.SetInt("_Cull", (int)CullMode.Off);
                    lineMaterial.SetInt("_ZWrite", 0);
                    lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                    lines = new Mesh { name = "Enemy Forge hitbox lines", hideFlags = HideFlags.HideAndDontSave };
                    lines.MarkDynamic();
                    var overlay = HiddenObject("Attack geometry");
                    overlay.transform.SetParent(root.transform, false);
                    overlay.AddComponent<MeshFilter>().sharedMesh = lines;
                    var renderer = overlay.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = lineMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    preview.AddSingleGO(root);
                    preview.camera.cameraType = CameraType.Preview;
                    preview.camera.clearFlags = CameraClearFlags.Color;
                    preview.camera.backgroundColor = new Color(0.065f, 0.083f, 0.11f, 1);
                    preview.camera.nearClipPlane = .01f;
                    preview.camera.farClipPlane = 10000;
                    preview.cameraFieldOfView = 35;
                    preview.ambientColor = new Color(.6f, .6f, .65f, 1);
                    preview.lights[0].intensity = 1.2f;
                    preview.lights[0].transform.rotation = Quaternion.Euler(35, 35, 0);
                    preview.lights[1].intensity = .75f;
                    preview.lights[1].transform.rotation = Quaternion.Euler(315, 215, 0);
                    pixels = new Texture2D(ImageSize, ImageSize, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                    modelBounds = Measure(model);
                    Directory.CreateDirectory(directory);
                    LoadDescriptors();
                }
                catch { Dispose(); throw; }
            }

            void LoadDescriptors()
            {
                lastDescriptorScan = EditorApplication.timeSinceStartup;
                var generated = Path.Combine(Application.dataPath, "_Project", "Scripts", "Enemy", "Generated");
                if (!Directory.Exists(generated)) return;
                foreach (var file in Directory.GetFiles(generated, "Attack.enemyattack.json", SearchOption.AllDirectories).Take(1000))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 1024 * 1024) continue;
                        var metadata = JObject.Parse(File.ReadAllText(file));
                        if ((int?)metadata["version"] == 1 && metadata["typeId"]?.Type == JTokenType.String)
                            descriptors[(string)metadata["typeId"]] = metadata;
                    }
                    catch (IOException) { }
                    catch (Newtonsoft.Json.JsonException) { }
                }
            }

            public object Frame(JObject payload)
            {
                if (disposed) throw new ObjectDisposedException("Enemy preview");
                var sequence = (long?)payload["sequence"] ?? -1;
                if (sequence < 0 || sequence <= lastSequence) throw new InvalidOperationException("미리보기 프레임 순서가 올바르지 않습니다.");
                var blueprint = payload["blueprint"] as JObject;
                if (blueprint == null) throw new ArgumentException("미리보기 설정이 없습니다.");
                if (Identity(blueprint["model"]) != modelIdentity) throw new InvalidOperationException("모델이 바뀌었습니다. 미리보기 세션을 다시 열어 주세요.");
                LastUse = EditorApplication.timeSinceStartup;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var time = Number(payload["time"], 0, 0, 36000);
                var clip = BaekhoEnemyForgeCatalog.Resolve(payload["clip"], typeof(AnimationClip)) as AnimationClip;
                var move = (string)payload["state"] == "Move" && BaekhoEnemyForgeAnimation.IsActive(blueprint["animation"] as JObject)
                    ? blueprint["animation"]?["move"] as JObject : null;
                var parameters = payload["moveParameters"] == null ? new Vector2(0, 1) : ReadMoveParameters(payload["moveParameters"]);
                var warning = Join(move != null ? SampleMove(move, parameters, time) : Sample(clip, time), materialWarning);
                SetModelTransform(blueprint);
                StableCameraBounds(blueprint, clip, time, move, parameters);
                var attack = FindAttack(blueprint, (string)payload["attackId"]);
                if (attack != null && !descriptors.ContainsKey((string)attack["typeId"] ?? "") && EditorApplication.timeSinceStartup - lastDescriptorScan > 1)
                    LoadDescriptors();
                var geometry = BuildAttackGeometry(attack, time, descriptors);
                if (!string.IsNullOrEmpty(geometry.Warning)) warning = Join(warning, geometry.Warning);
                root.transform.SetPositionAndRotation(geometry.ActorPosition, Quaternion.identity);
                var draw = new WireGeometry();
                draw.Grid(Mathf.Max(2, modelBounds.size.magnitude), -geometry.ActorPosition);
                if ((bool?)payload["showHitboxes"] == true) draw.Attack(geometry);
                var hpPosition = Vector(blueprint["hpBarPosition"], new Vector3(0, 3, 0));
                var vfxPosition = Vector(blueprint["vfxPosition"], Vector3.up);
                var anchorWidth = Mathf.Clamp(modelBounds.size.x * .55f, .4f, 2);
                draw.Anchors(hpPosition, vfxPosition, anchorWidth);
                lines.Clear();
                lines.SetVertices(draw.Vertices);
                lines.SetColors(draw.Colors);
                lines.SetIndices(draw.Indices.ToArray(), MeshTopology.Lines, 0);
                lines.RecalculateBounds();

                var framing = cameraBounds;
                framing.Encapsulate(cameraBounds.center + geometry.TravelEnd);
                framing.Encapsulate(hpPosition + Vector3.right * anchorWidth * .5f);
                framing.Encapsulate(hpPosition - Vector3.right * anchorWidth * .5f);
                framing.Encapsulate(vfxPosition);
                // Keep the camera on the authored character, rather than allowing a giant range ring to hide it.
                var radius = Mathf.Max(.3f, framing.extents.magnitude);
                var yaw = Number(payload["yaw"], 35, -360000, 360000);
                var pitch = Number(payload["pitch"], 15, -85, 85);
                var zoom = Number(payload["zoom"], 1, .15f, 10);
                var distance = radius / Mathf.Sin(preview.cameraFieldOfView * Mathf.Deg2Rad * .5f) * 1.15f / zoom;
                var target = framing.center;
                var direction = Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -1);
                preview.camera.transform.position = target + direction * distance;
                preview.camera.transform.LookAt(target);
                var imagePath = Path.Combine(directory, sequence.ToString("D12") + ".jpg");
                Render(imagePath);
                lastSequence = sequence;
                frameFiles.Enqueue(imagePath);
                while (frameFiles.Count > 3) DeleteFile(frameFiles.Dequeue());
                return new { sequence, imagePath = imagePath.Replace('\\', '/'), warning = string.IsNullOrEmpty(warning) ? null : warning };
            }

            string Sample(AnimationClip clip, float time)
            {
                if (clip != currentClip || currentMove != null)
                {
                    ResetSampling();
                    currentClip = clip;
                    clipWarning = null;
                    if (clip)
                    {
                        if (clip.legacy) return clipWarning = "Legacy 클립은 이 미리보기에서 재생할 수 없습니다. 모델의 기본 자세를 표시합니다.";
                        if (clip.humanMotion && (!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman))
                            return clipWarning = "선택한 Humanoid 클립과 모델의 Avatar가 맞지 않습니다. 모델의 기본 자세를 표시합니다.";
                        if (!clip.humanMotion)
                        {
                            var bindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform) || b.type == typeof(SkinnedMeshRenderer)).ToArray();
                            var missing = bindings.Count(b => !string.IsNullOrEmpty(b.path) && !animator.transform.Find(b.path));
                            if (bindings.Length > 0 && missing == bindings.Length)
                                return clipWarning = "Generic 클립의 바인딩 경로가 모델 계층과 맞지 않습니다. 모델의 기본 자세를 표시합니다.";
                            if (missing > 0) clipWarning = "Generic 클립의 일부 바인딩 경로가 모델에 없습니다. 연결되는 부분만 재생합니다.";
                        }
                        graph = PlayableGraph.Create("Enemy Forge preview " + Id);
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        playable = AnimationClipPlayable.Create(graph, clip);
                        playable.SetApplyFootIK(false);
                        playable.SetApplyPlayableIK(false);
                        var output = AnimationPlayableOutput.Create(graph, "Preview", animator);
                        output.SetSourcePlayable(playable);
                        graph.Play();
                    }
                }
                if (clip && !graph.IsValid())
                    return clipWarning ?? "선택 클립의 Rig와 모델 Avatar를 확인해 주세요. 모델의 기본 자세를 표시합니다.";
                if (graph.IsValid())
                {
                    playable.SetTime(Math.Min(time, clip.length));
                    graph.Evaluate(0);
                }
                return clip ? clipWarning : "클립을 선택하면 애니메이션을 재생할 수 있습니다.";
            }

            string SampleMove(JObject move, Vector2 parameters, float time)
            {
                var identity = move.ToString(Newtonsoft.Json.Formatting.None);
                if (identity != currentMove)
                {
                    ResetSampling();
                    currentMove = identity;
                    try
                    {
                        moveController = BaekhoEnemyForgeAnimation.CreatePreviewController(move);
                        foreach (var clip in moveController.animationClips)
                        {
                            if (clip.legacy) throw new InvalidOperationException("Blend Tree에 Legacy 클립을 사용할 수 없습니다.");
                            if (clip.humanMotion && (!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman))
                                throw new InvalidOperationException("Blend Tree의 Humanoid 클립과 모델 Avatar가 맞지 않습니다.");
                            if (!clip.humanMotion)
                            {
                                var bindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform) || b.type == typeof(SkinnedMeshRenderer)).ToArray();
                                var missing = bindings.Count(b => !string.IsNullOrEmpty(b.path) && !animator.transform.Find(b.path));
                                if (bindings.Length > 0 && missing == bindings.Length)
                                    throw new InvalidOperationException("Blend Tree 클립 " + clip.name + "의 본 경로가 모델과 맞지 않습니다.");
                                if (missing > 0) clipWarning = "Blend Tree 클립의 일부 본 경로가 모델에 없습니다. 연결되는 부분만 재생합니다.";
                            }
                        }
                        graph = PlayableGraph.Create("Enemy Forge Move preview " + Id);
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        movePlayable = AnimatorControllerPlayable.Create(graph, moveController);
                        var output = AnimationPlayableOutput.Create(graph, "Move Blend Tree", animator);
                        output.SetSourcePlayable(movePlayable);
                        graph.Play();
                    }
                    catch (Exception exception)
                    {
                        clipWarning = "Move 미리보기: " + exception.GetBaseException().Message;
                        if (graph.IsValid()) graph.Destroy();
                        if (moveController) BaekhoEnemyForgeAnimation.DestroyPreviewController(moveController);
                        moveController = null;
                    }
                }
                if (!graph.IsValid()) return clipWarning;
                movePlayable.SetFloat((string)move["parameterX"], parameters.x);
                movePlayable.SetFloat((string)move["parameterY"], parameters.y);
                graph.Evaluate(0);
                movePlayable.PlayInFixedTime("Move", 0, time);
                graph.Evaluate(0);
                return clipWarning;
            }

            void ResetSampling()
            {
                if (graph.IsValid()) graph.Destroy();
                if (moveController) BaekhoEnemyForgeAnimation.DestroyPreviewController(moveController);
                moveController = null;
                currentMove = null;
                currentClip = null;
                clipWarning = null;
                foreach (var pair in bindPose) pair.Value.Restore(pair.Key);
            }

            void SetModelTransform(JObject blueprint)
            {
                var transform = blueprint["modelTransform"] as JObject;
                model.transform.localPosition = Vector(transform?["position"], Vector3.zero);
                model.transform.localRotation = Quaternion.Euler(Vector(transform?["rotation"], Vector3.zero));
                model.transform.localScale = Vector(transform?["scale"], Vector3.one);
                // Re-measure only the model; attack overlays have different world-unit scaling.
                modelBounds = Measure(model);
            }

            void StableCameraBounds(JObject blueprint, AnimationClip clip, float requestedTime, JObject move, Vector2 parameters)
            {
                var identity = (move != null ? currentMove + ":" + parameters.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + parameters.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    : clip ? clip.GetInstanceID().ToString() : "none") + ":" + blueprint["modelTransform"]?.ToString(Newtonsoft.Json.Formatting.None);
                if (identity == boundsIdentity) return;
                cameraBounds = modelBounds;
                if ((move != null || clip) && graph.IsValid())
                {
                    var duration = move != null ? moveController.animationClips.Select(c => c.length).DefaultIfEmpty(1f).Max() : clip.length;
                    // Frame the whole motion once, keeping the camera still during playback and scrubbing.
                    for (var i = 0; i <= 4; i++)
                    {
                        if (move != null) SampleMove(move, parameters, duration * i / 4f);
                        else { playable.SetTime(duration * i / 4d); graph.Evaluate(0); }
                        SetModelTransform(blueprint);
                        cameraBounds.Encapsulate(modelBounds);
                    }
                    if (move != null) SampleMove(move, parameters, requestedTime);
                    else Sample(clip, requestedTime);
                    SetModelTransform(blueprint);
                }
                boundsIdentity = identity;
            }

            void Render(string output)
            {
                preview.BeginPreview(new Rect(0, 0, ImageSize, ImageSize), GUIStyle.none);
                Texture rendered;
                try { preview.Render(true); }
                finally { rendered = preview.EndPreview(); }
                var previous = RenderTexture.active;
                var temporary = RenderTexture.GetTemporary(ImageSize, ImageSize, 0, RenderTextureFormat.ARGB32);
                try
                {
                    Graphics.Blit(rendered, temporary);
                    RenderTexture.active = temporary;
                    pixels.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0, false);
                    pixels.Apply(false, false);
                    var staging = output + ".tmp";
                    File.WriteAllBytes(staging, pixels.EncodeToJPG(85));
                    File.Move(staging, output);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(temporary);
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (graph.IsValid()) graph.Destroy();
                if (moveController) BaekhoEnemyForgeAnimation.DestroyPreviewController(moveController);
                if (preview != null) preview.Cleanup();
                // Cleanup owns objects added to the preview; construction failures may happen before AddSingleGO.
                if (root) Object.DestroyImmediate(root);
                if (lines) Object.DestroyImmediate(lines);
                if (lineMaterial) Object.DestroyImmediate(lineMaterial);
                if (pixels) Object.DestroyImmediate(pixels);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        struct Pose
        {
            readonly Vector3 position, scale;
            readonly Quaternion rotation;
            public Pose(Transform t) { position = t.localPosition; scale = t.localScale; rotation = t.localRotation; }
            public void Restore(Transform t) { t.localPosition = position; t.localRotation = rotation; t.localScale = scale; }
        }

        static GameObject HiddenObject(string name) => new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };

        internal static GameObject ResolveRenderSource(JObject blueprint, GameObject selectedModel)
        {
            var template = BaekhoEnemyForgeCatalog.Resolve(blueprint["template"], typeof(GameObject)) as GameObject;
            if (!template) return selectedModel;
            // The original prefab can override an FBX's empty/imported material slots, visibility and Avatar.
            // Read that hierarchy directly; cloning only its rendering components still never runs its scripts.
            foreach (Transform candidate in template.transform)
            {
                if (!candidate.GetComponentInChildren<Animator>(true)) continue;
                var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(candidate.gameObject);
                if (original == selectedModel || original && AssetDatabase.GetAssetPath(original) == AssetDatabase.GetAssetPath(selectedModel))
                    return candidate.gameObject;
            }
            return selectedModel;
        }

        internal static Transform CloneTransforms(Transform source, Transform parent, Dictionary<Transform, Transform> map)
        {
            var clone = HiddenObject(source.name);
            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = source.localPosition;
            clone.transform.localRotation = source.localRotation;
            clone.transform.localScale = source.localScale;
            map.Add(source, clone.transform);
            foreach (Transform child in source) CloneTransforms(child, clone.transform, map);
            clone.SetActive(source.gameObject.activeSelf);
            return clone.transform;
        }

        internal static void CopyRenderers(Dictionary<Transform, Transform> map)
        {
            foreach (var pair in map)
            {
                var source = pair.Key;
                var target = pair.Value.gameObject;
                if (source.TryGetComponent<MeshFilter>(out var filter)) target.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                if (source.TryGetComponent<MeshRenderer>(out var renderer)) CopyRenderer(renderer, target.AddComponent<MeshRenderer>());
                if (!source.TryGetComponent<SkinnedMeshRenderer>(out var skin)) continue;
                var copy = target.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = skin.sharedMesh;
                copy.bones = skin.bones.Select(b => b && map.TryGetValue(b, out var mapped) ? mapped : null).ToArray();
                copy.rootBone = skin.rootBone && map.TryGetValue(skin.rootBone, out var rootBone) ? rootBone : null;
                copy.localBounds = skin.localBounds;
                copy.updateWhenOffscreen = true;
                copy.forceMatrixRecalculationPerRender = true;
                copy.quality = skin.quality;
                if (skin.sharedMesh)
                    for (var i = 0; i < skin.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                CopyRenderer(skin, copy);
            }
        }

        static void CopyRenderer(Renderer source, Renderer target)
        {
            target.sharedMaterials = source.sharedMaterials;
            target.enabled = source.enabled;
            target.shadowCastingMode = ShadowCastingMode.Off;
            target.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            source.GetPropertyBlock(block);
            target.SetPropertyBlock(block);
        }

        static Bounds Measure(GameObject model)
        {
            var found = false;
            var result = new Bounds();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!found) { result = renderer.bounds; found = true; }
                else result.Encapsulate(renderer.bounds);
            }
            if (!found || result.size.sqrMagnitude < .000001f) throw new InvalidOperationException("모델에서 표시할 활성 메시를 찾지 못했습니다.");
            return result;
        }

        static string Identity(JToken reference) => reference == null || reference.Type == JTokenType.Null ? "" : (string)reference["guid"] + ":" + (string)reference["localId"];
        static string Join(string a, string b) => string.IsNullOrEmpty(a) ? b : a + " " + b;
        static void DeleteFile(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        static float Number(JToken token, float fallback, float min = -100000, float max = 100000)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw new ArgumentException("미리보기 수치 형식이 올바르지 않습니다.");
            var value = (float)token;
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("미리보기 수치는 유한한 값이어야 합니다.");
            return Mathf.Clamp(value, min, max);
        }
        static Vector2 ReadMoveParameters(JToken token)
        {
            var values = BaekhoEnemyForgeCatalog.Numbers(token, 2);
            return new Vector2(values[0], values[1]);
        }
        static Vector3 Vector(JToken token, Vector3 fallback)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token is JArray array && array.Count == 3) return new Vector3(Number(array[0], 0), Number(array[1], 0), Number(array[2], 0));
            if (token is JObject obj) return new Vector3(Number(obj["x"], fallback.x), Number(obj["y"], fallback.y), Number(obj["z"], fallback.z));
            throw new ArgumentException("미리보기 벡터 형식이 올바르지 않습니다.");
        }

        static JObject FindAttack(JObject blueprint, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var attack in (blueprint["attacks"] as JArray ?? new JArray()).OfType<JObject>())
                if ((string)attack["id"] == id) return attack;
            return (string)blueprint["selfDestruct"]?["id"] == id ? blueprint["selfDestruct"] as JObject : null;
        }

        internal sealed class AttackGeometry
        {
            public readonly List<Shape> Shapes = new List<Shape>();
            public Vector3 ActorPosition, TravelEnd;
            public float ExecuteRange, MinimumRange;
            public string Warning;
        }
        internal sealed class Shape
        {
            public string Kind;
            public Vector3 Offset, Size;
            public Vector3[] Points;
            public float Radius, Height, Start, End;
            public bool Active;
        }

        internal static AttackGeometry BuildAttackGeometry(JObject attack, float time, Dictionary<string, JObject> descriptors)
        {
            var result = new AttackGeometry();
            if (attack == null) return result;
            var values = attack["values"] as JObject ?? new JObject();
            var type = (string)attack["typeId"] ?? "";
            result.ExecuteRange = Number(values["executeRange"], 0, 0);
            result.MinimumRange = Number(values["minimumRange"], 0, 0);
            if (type == "MonsterDefaultAttackSO")
            {
                var start = Number(values["hitStartTime"], .1f, 0);
                var duration = Number(values["hitDuration"], .2f, 0);
                Add(result, values, start, Mathf.Min(start + duration, Number(values["attackDuration"], 1, 0)), time, null, true);
            }
            else if (type == "MonsterComboAttackSO")
            {
                foreach (var hit in (values["hits"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var start = Number(hit["hitStartTime"], 0, 0);
                    Add(result, values, start, Mathf.Min(start + Number(hit["hitDuration"], 0, 0), Number(values["attackDuration"], 1.8f, 0)), time, hit["hitBoxOffset"], false);
                }
                if (Number(values["hitBoxShape"], 0) != 0) result.Warning = "현재 게임의 콤보 공격은 설정된 모양과 관계없이 구형 판정을 사용합니다.";
            }
            else if (type == "MonsterDashAttackSO")
            {
                var start = Number(values["startupTime"], .2f, 0);
                var duration = Number(values["dashDuration"], .4f, 0);
                var distance = Number(values["dashSpeed"], 15, 0) * duration;
                result.TravelEnd = Vector3.forward * distance;
                result.ActorPosition = Vector3.forward * Number(values["dashSpeed"], 15, 0) * Mathf.Clamp(time - start, 0, duration);
                Add(result, values, start, start + duration, time, null, false);
                result.Shapes.Add(new Shape { Kind = "path", Points = new[] { -result.ActorPosition, result.TravelEnd - result.ActorPosition }, Active = false });
                result.Warning = "돌진 경로는 장애물 없는 전방 기준입니다. 실제 타깃 방향과 NavMesh 충돌은 게임에서 결정됩니다.";
            }
            else if (type == "MonsterJumpSlamSO")
            {
                var startup = Number(values["startupTime"], .3f, 0);
                var duration = Number(values["jumpDuration"], .65f, .01f);
                var progress = Mathf.Clamp01((time - startup) / duration);
                result.TravelEnd = Vector3.forward * 3;
                result.ActorPosition = result.TravelEnd * progress + Vector3.up * Number(values["jumpHeight"], 5, 0) * Mathf.Sin(progress * Mathf.PI);
                var landing = startup + duration;
                result.Shapes.Add(new Shape { Kind = "sphere", Radius = Number(values["slamRadius"], 2.5f, 0), Offset = Vector(values["hitBoxOffset"], Vector3.zero) + result.TravelEnd - result.ActorPosition, Start = landing, End = landing, Active = Mathf.Abs(time - landing) <= 1f / 24 });
                var points = new Vector3[33];
                for (var i = 0; i < points.Length; i++) { var t = i / 32f; points[i] = result.TravelEnd * t + Vector3.up * Number(values["jumpHeight"], 5, 0) * Mathf.Sin(t * Mathf.PI) - result.ActorPosition; }
                result.Shapes.Add(new Shape { Kind = "path", Points = points });
                result.Warning = "점프 경로는 타깃이 없을 때의 전방 3m 기준입니다. 착지 판정은 한 번 발생하며 실제 위치는 타깃과 NavMesh에 따라 달라집니다.";
            }
            else if (type == "MonsterSelfDestructSO")
            {
                var speed = Number(values["attackSpeed"], 1, .01f);
                var explosion = Number(values["explosionDelay"], 3, 0) * speed;
                result.Shapes.Add(new Shape { Kind = "sphere", Offset = Vector(values["explosionOffset"], Vector3.zero), Radius = Number(values["explosionRadius"], 3, 0), Start = explosion, End = explosion, Active = Mathf.Abs(time - explosion) <= 1f / 24 });
                result.Warning = "자폭은 폭발 시점에 한 번 판정합니다. 지연 시간은 공격 속도와 무관한 게임 시간이며, 현재 타임라인은 클립 기준 초입니다.";
            }
            else if (descriptors != null && descriptors.TryGetValue(type, out var descriptor) && (bool?)descriptor["previewSupported"] == true)
            {
                foreach (var item in (descriptor["preview"]?["shapes"] as JArray ?? new JArray()).OfType<JObject>().Take(128))
                {
                    var start = Number(item["start"], 0, 0);
                    var end = Number(item["end"], start, start);
                    var shape = new Shape { Kind = (string)item["shape"], Offset = Vector(item["offset"], Vector3.zero), Radius = Number(item["radius"], 1, 0), Height = Number(item["height"], 2, 0), Size = Vector(item["size"], Vector3.one), Start = start, End = end, Active = Active(time, start, end) };
                    if (item["points"] is JArray points) shape.Points = points.Take(512).Select(p => Vector(p, Vector3.zero)).ToArray();
                    if (!new[] { "sphere", "box", "capsule", "path" }.Contains(shape.Kind)) continue;
                    result.Shapes.Add(shape);
                }
                result.Warning = "사용자 공격은 생성 시 저장한 선언형 미리보기입니다. 런타임 코드·타깃 추적·실제 충돌은 실행하지 않습니다.";
                if (descriptor["defaults"] is JObject defaults && !EquivalentDefaults(defaults, values))
                    result.Warning = Join(result.Warning, "AI 미리보기는 생성 당시 기본값 기준입니다. 변경한 매개변수는 선언형 도형에 자동 반영되지 않습니다.");
            }
            else result.Warning = "이 사용자 공격에는 지원되는 히트박스 미리보기 정보가 없습니다. 모델과 애니메이션만 표시합니다.";
            return result;
        }

        static bool Active(float time, float start, float end) => end > start ? time >= start && time < end : Mathf.Abs(time - start) <= 1f / 24;
        static bool EquivalentDefaults(JToken expected, JToken actual)
        {
            if (expected == null || expected.Type == JTokenType.Null) return actual == null || actual.Type == JTokenType.Null;
            if (actual == null || actual.Type == JTokenType.Null) return false;
            if ((expected.Type == JTokenType.Float || expected.Type == JTokenType.Integer) && (actual.Type == JTokenType.Float || actual.Type == JTokenType.Integer))
                return Math.Abs((double)expected - (double)actual) <= .000001;
            if (expected is JObject properties)
                return actual is JObject values && properties.Properties().All(p => EquivalentDefaults(p.Value, values[p.Name]));
            if (expected is JArray array)
                return actual is JArray items && array.Count == items.Count && array.Select((v, i) => EquivalentDefaults(v, items[i])).All(equal => equal);
            return JToken.DeepEquals(expected, actual);
        }
        static void Add(AttackGeometry result, JObject values, float start, float end, float time, JToken offset, bool supportsShape)
        {
            var kind = supportsShape ? (int)Number(values["hitBoxShape"], 0, 0, 2) : 0;
            result.Shapes.Add(new Shape { Kind = kind == 1 ? "box" : kind == 2 ? "capsule" : "sphere", Offset = Vector(offset ?? values["hitBoxOffset"], new Vector3(0, 0, 1)), Radius = Number(values["hitBoxRadius"], 1.2f, 0), Size = Vector(values["hitBoxSize"], Vector3.one), Height = Number(values["hitBoxHeight"], 2, 0), Start = start, End = end, Active = end > start && Active(time, start, end) });
        }

        sealed class WireGeometry
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Indices = new List<int>();
            static readonly Color Idle = new Color(1, .65f, .15f, .65f), Live = new Color(1, .18f, .12f, 1), Range = new Color(.2f, .8f, .75f, .55f);
            void Line(Vector3 a, Vector3 b, Color color) { var i = Vertices.Count; Vertices.Add(a); Vertices.Add(b); Colors.Add(color); Colors.Add(color); Indices.Add(i); Indices.Add(i + 1); }
            void Circle(Vector3 center, Vector3 x, Vector3 y, float radius, Color color)
            {
                for (var i = 0; i < 48; i++) { var a = i * Mathf.PI * 2 / 48; var b = (i + 1) * Mathf.PI * 2 / 48; Line(center + radius * (x * Mathf.Cos(a) + y * Mathf.Sin(a)), center + radius * (x * Mathf.Cos(b) + y * Mathf.Sin(b)), color); }
            }
            void Sphere(Vector3 center, float radius, Color color) { Circle(center, Vector3.right, Vector3.up, radius, color); Circle(center, Vector3.right, Vector3.forward, radius, color); Circle(center, Vector3.up, Vector3.forward, radius, color); }
            void Box(Vector3 center, Vector3 size, Color color)
            {
                var half = size * .5f;
                for (var axis = 0; axis < 3; axis++) for (var i = 0; i < 4; i++)
                {
                    var a = new Vector3(); var b = new Vector3();
                    a[axis] = -half[axis]; b[axis] = half[axis];
                    a[(axis + 1) % 3] = b[(axis + 1) % 3] = (i % 2 == 0 ? -1 : 1) * half[(axis + 1) % 3];
                    a[(axis + 2) % 3] = b[(axis + 2) % 3] = (i < 2 ? -1 : 1) * half[(axis + 2) % 3];
                    Line(center + a, center + b, color);
                }
            }
            void Capsule(Vector3 center, float radius, float height, Color color)
            {
                // Runtime uses +/- (height/2-radius), including reversed endpoints for short heights.
                var extent = Mathf.Abs(height * .5f - radius);
                Circle(center + Vector3.up * extent, Vector3.right, Vector3.forward, radius, color);
                Circle(center - Vector3.up * extent, Vector3.right, Vector3.forward, radius, color);
                for (var direction = 0; direction < 4; direction++)
                {
                    var radial = Quaternion.Euler(0, direction * 90, 0) * Vector3.right;
                    Line(center + radial * radius + Vector3.up * extent, center + radial * radius - Vector3.up * extent, color);
                    for (var i = 0; i < 24; i++)
                    {
                        var a = i * Mathf.PI / 24; var b = (i + 1) * Mathf.PI / 24;
                        var sign = direction < 2 ? 1 : -1;
                        Line(center + Vector3.up * extent * sign + radius * (radial * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a) * sign), center + Vector3.up * extent * sign + radius * (radial * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b) * sign), color);
                    }
                }
            }
            public void Grid(float extent, Vector3 offset)
            {
                var spacing = Mathf.Max(.5f, Mathf.Pow(10, Mathf.Floor(Mathf.Log10(extent))) * .5f);
                var color = new Color(.3f, .4f, .5f, .18f);
                for (var i = -10; i <= 10; i++) { Line(offset + new Vector3(i * spacing, 0, -10 * spacing), offset + new Vector3(i * spacing, 0, 10 * spacing), color); Line(offset + new Vector3(-10 * spacing, 0, i * spacing), offset + new Vector3(10 * spacing, 0, i * spacing), color); }
            }
            public void Anchors(Vector3 hp, Vector3 vfx, float width)
            {
                var green = new Color(.25f, 1, .4f, 1);
                Box(hp, new Vector3(width, Mathf.Max(.04f, width * .07f), .025f), green);
                Line(hp - Vector3.right * width * .5f, hp + Vector3.right * width * .5f, green);
                var cyan = new Color(.25f, .8f, 1, 1);
                var radius = Mathf.Max(.06f, width * .09f);
                Line(vfx - Vector3.right * radius, vfx + Vector3.right * radius, cyan);
                Line(vfx - Vector3.up * radius, vfx + Vector3.up * radius, cyan);
                Line(vfx - Vector3.forward * radius, vfx + Vector3.forward * radius, cyan);
            }
            public void Attack(AttackGeometry geometry)
            {
                if (geometry.ExecuteRange > 0) Circle(-geometry.ActorPosition, Vector3.right, Vector3.forward, geometry.ExecuteRange, Range);
                if (geometry.MinimumRange > 0) Circle(-geometry.ActorPosition, Vector3.right, Vector3.forward, geometry.MinimumRange, Idle);
                foreach (var shape in geometry.Shapes)
                {
                    var color = shape.Active ? Live : Idle;
                    if (shape.Kind == "sphere") Sphere(shape.Offset, shape.Radius, color);
                    else if (shape.Kind == "box") Box(shape.Offset, shape.Size, color);
                    else if (shape.Kind == "capsule") Capsule(shape.Offset, shape.Radius, shape.Height, color);
                    else if (shape.Kind == "path" && shape.Points != null)
                        for (var i = 1; i < shape.Points.Length; i++) Line(shape.Points[i - 1] + shape.Offset, shape.Points[i] + shape.Offset, Range);
                }
            }
        }

        /// <summary>No asset writes, scene changes, or NUnit dependency. Invokable by integration smoke tests.</summary>
        public static object RunGeometryRegression()
        {
            var descriptors = new Dictionary<string, JObject>();
            Func<string, JObject, JObject> attack = (type, values) => new JObject { ["typeId"] = type, ["values"] = values };
            var basic = attack("MonsterDefaultAttackSO", new JObject { ["hitBoxShape"] = 1, ["hitBoxSize"] = new JArray(2, 3, 4), ["hitBoxOffset"] = new JArray(0, 1, 2), ["hitStartTime"] = .5, ["hitDuration"] = .2, ["attackDuration"] = 1 });
            var before = BuildAttackGeometry(basic, .49f, descriptors).Shapes[0];
            var during = BuildAttackGeometry(basic, .6f, descriptors).Shapes[0];
            if (before.Active || !during.Active || during.Kind != "box" || during.Size != new Vector3(2, 3, 4) || during.Offset != new Vector3(0, 1, 2)) throw new Exception("Basic hit window or world-unit box regression.");
            var dash = BuildAttackGeometry(attack("MonsterDashAttackSO", new JObject { ["startupTime"] = .2, ["dashDuration"] = .4, ["dashSpeed"] = 10, ["hitBoxShape"] = 2 }), .4f, descriptors);
            if (dash.Shapes[0].Kind != "sphere" || Mathf.Abs(dash.ActorPosition.z - 2) > .001f || Mathf.Abs(dash.TravelEnd.z - 4) > .001f) throw new Exception("Dash runtime sphere or travel regression.");
            var combo = BuildAttackGeometry(attack("MonsterComboAttackSO", new JObject { ["hitBoxShape"] = 1, ["hits"] = new JArray(new JObject { ["hitStartTime"] = .2, ["hitDuration"] = .1, ["hitBoxOffset"] = new JArray(1, 2, 3) }) }), .25f, descriptors);
            if (combo.Shapes[0].Kind != "sphere" || !combo.Shapes[0].Active || combo.Shapes[0].Offset != new Vector3(1, 2, 3)) throw new Exception("Combo actual overlap regression.");
            var destruct = BuildAttackGeometry(attack("MonsterSelfDestructSO", new JObject { ["explosionDelay"] = 2, ["attackSpeed"] = 2 }), 4, descriptors);
            if (!destruct.Shapes[0].Active || destruct.Shapes[0].Start != 4) throw new Exception("Self destruct world-time conversion regression.");
            descriptors["Custom"] = new JObject { ["previewSupported"] = true, ["defaults"] = new JObject { ["damage"] = 1 }, ["preview"] = new JObject { ["shapes"] = new JArray(new JObject { ["shape"] = "box", ["start"] = 1, ["end"] = 2, ["size"] = new JArray(1, 2, 3) }) } };
            if (!BuildAttackGeometry(attack("Custom", new JObject()), 1.5f, descriptors).Shapes[0].Active) throw new Exception("Declarative custom preview regression.");
            if (!BuildAttackGeometry(attack("Custom", new JObject { ["damage"] = 2 }), 1.5f, descriptors).Warning.Contains("생성 당시 기본값")) throw new Exception("Modified custom parameters need a preview warning.");
            if (!EquivalentDefaults(new JObject { ["value"] = 1 }, new JObject { ["value"] = 1.0 })) throw new Exception("Integer/float JSON defaults must compare equally.");
            if (string.IsNullOrEmpty(BuildAttackGeometry(attack("Unknown", new JObject()), 0, descriptors).Warning)) throw new Exception("Unsupported preview must be explicit.");
            return new { passed = true, cases = 8, activeSessions = Sessions.Count };
        }
    }
}
#endif
