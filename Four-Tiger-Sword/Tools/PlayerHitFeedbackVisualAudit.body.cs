var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
if (player == null) throw new System.InvalidOperationException("A live player is required for the reaction audit.");
var source = player.Animator != null ? player.Animator : player.GetComponentInChildren<UnityEngine.Animator>();
if (source == null) throw new System.InvalidOperationException("The live player's model Animator was not found.");
var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/_Project/Animations/AnimController/BDJ.controller");
if (controller == null) throw new System.InvalidOperationException("The BDJ Animator controller was not found.");
var preview = new UnityEditor.PreviewRenderUtility(true);
var meshes = new System.Collections.Generic.List<UnityEngine.Mesh>();
UnityEngine.Texture2D sheet = null, panelTexture = null;
var previousRenderTarget = UnityEngine.RenderTexture.active;
var log = new System.Text.StringBuilder();
try
{
    var staging = new UnityEngine.GameObject("Reaction audit staging");
    staging.SetActive(false); preview.AddSingleGO(staging);
    var model = UnityEngine.Object.Instantiate(source.gameObject, staging.transform, false);
    foreach (var script in model.GetComponentsInChildren<UnityEngine.MonoBehaviour>(true)) script.enabled = false;
    foreach (var camera in model.GetComponentsInChildren<UnityEngine.Camera>(true)) camera.enabled = false;
    foreach (var light in model.GetComponentsInChildren<UnityEngine.Light>(true)) light.enabled = false;
    model.transform.SetParent(null, false); preview.AddSingleGO(model);
    model.transform.SetPositionAndRotation(UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity);
    model.transform.localScale = source.transform.lossyScale; model.SetActive(true);
    var animator = model.GetComponent<UnityEngine.Animator>();
    animator.runtimeAnimatorController = controller; animator.enabled = true; animator.speed = 1f;
    animator.applyRootMotion = false; animator.fireEvents = false;
    animator.cullingMode = UnityEngine.AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0f);
    var skins = model.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
    for (int i = 0; i < skins.Length; i++)
    {
        bool visible = skins[i].enabled; skins[i].enabled = false;
        var baked = new UnityEngine.GameObject("Baked audit mesh"); baked.transform.SetParent(skins[i].transform, false);
        var mesh = new UnityEngine.Mesh(); meshes.Add(mesh);
        baked.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
        var renderer = baked.AddComponent<UnityEngine.MeshRenderer>();
        renderer.sharedMaterials = skins[i].sharedMaterials; renderer.enabled = visible;
    }
    string[] states = { "Idle", "Hit", "Hit", "Hit", "Knockdown", "Knockdown", "GetUp", "GetUp" };
    float[] samples = { 0f, .02f, .06f, .12f, .5f, .999f, .5f, .999f };
    var normalized = new float[states.Length];
    for (int i = 0; i < states.Length; i++)
    {
        var state = System.Linq.Enumerable.First(controller.layers[0].stateMachine.states, item => item.state.name == states[i]).state;
        var clip = state.motion as UnityEngine.AnimationClip;
        if (clip == null) throw new System.InvalidOperationException(states[i] + " requires an AnimationClip.");
        normalized[i] = states[i] == "Hit" ? samples[i] / clip.length : samples[i];
        log.AppendLine("panel=" + (i + 1) + " state=" + states[i] + " clip=" + clip.name + " sample=" + samples[i] + (states[i] == "Hit" ? "s" : " normalized"));
    }
    var bounds = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one);
    bool hasBounds = false;
    for (int pass = 0; pass < 2; pass++)
    {
        for (int i = 0; i < states.Length; i++)
        {
            animator.Rebind(); animator.Play(states[i], 0, normalized[i]); animator.Update(0f);
            for (int j = 0; j < skins.Length; j++) skins[j].BakeMesh(meshes[j]);
            if (pass == 0)
            {
                foreach (var renderer in model.GetComponentsInChildren<UnityEngine.MeshRenderer>())
                    if (renderer.enabled) { if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds); }
                continue;
            }
            const int size = 440;
            preview.BeginStaticPreview(new UnityEngine.Rect(0, 0, size, size)); preview.Render(true, true);
            panelTexture = preview.EndStaticPreview();
            sheet.SetPixels(i % 4 * size, (1 - i / 4) * size, size, size, panelTexture.GetPixels());
            UnityEngine.Object.DestroyImmediate(panelTexture); panelTexture = null;
        }
        if (pass != 0) continue;
        preview.cameraFieldOfView = 33f; preview.camera.nearClipPlane = .05f; preview.camera.farClipPlane = 100f;
        preview.camera.clearFlags = UnityEngine.CameraClearFlags.Color; preview.camera.backgroundColor = new UnityEngine.Color(.14f, .16f, .19f, 1f);
        float distance = UnityEngine.Mathf.Max(3f, bounds.extents.magnitude / UnityEngine.Mathf.Sin(16.5f * UnityEngine.Mathf.Deg2Rad) * 1.15f);
        preview.camera.transform.position = bounds.center + new UnityEngine.Vector3(4f, 3.3f, 5f).normalized * distance;
        preview.camera.transform.LookAt(bounds.center);
        preview.lights[0].intensity = 1.5f; preview.lights[0].transform.rotation = UnityEngine.Quaternion.Euler(40f, 40f, 0f);
        preview.lights[1].intensity = 1.3f; preview.lights[1].transform.rotation = UnityEngine.Quaternion.Euler(340f, 218f, 177f);
        preview.ambientColor = UnityEngine.Color.gray; sheet = new UnityEngine.Texture2D(1760, 880, UnityEngine.TextureFormat.RGB24, false);
    }
    sheet.Apply();
    string path = System.IO.Path.GetFullPath("PlayerHitFeedbackVerification/reactions.png");
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
    return path + "\n" + log;
}
finally
{
    if (panelTexture != null) UnityEngine.Object.DestroyImmediate(panelTexture);
    if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
    preview.Cleanup();
    foreach (var mesh in meshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
    UnityEngine.RenderTexture.active = previousRenderTarget;
}
