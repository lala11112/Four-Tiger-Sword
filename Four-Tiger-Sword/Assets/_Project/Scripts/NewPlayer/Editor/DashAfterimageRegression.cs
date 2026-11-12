using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises dash afterimages with a posed, scaled skinned mesh.</summary>
public static class DashAfterimageRegression
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class ProbeForm : BaseForm
    {
        public override ElementType Element { get; }
        public ProbeForm(ElementType element) : base(null) => Element = element;
    }

    private sealed class NeutralState : IPlayerState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Dash Afterimage Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Dash afterimage regression requires Edit Mode.");

        var scene = EditorSceneManager.NewPreviewScene();
        GameObject host = null;
        PlayerController player = null;
        Mesh mesh = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Dash afterimage: " + message);
            checks++;
        }

        try
        {
            host = new GameObject("__DashAfterimageRegression");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.SetActive(false);
            host.transform.position = new Vector3(3000f, 1500f, 3000f);
            host.transform.localScale = new Vector3(1.4f, 1.8f, 2.2f);
            player = host.AddComponent<PlayerController>();
            Property(player, "Input", host.GetComponent<PlayerInputHandler>());
            Property(player, "StatManager", host.GetComponent<PlayerStatManager>());
            Property(player, "Controller", host.GetComponent<CharacterController>());
            Property(player, "Movement", host.GetComponent<PlayerMovement>());
            player.Movement.Initialize(player);

            var model = Child(host.transform, "Model");
            Property(player, "Animator", model.AddComponent<Animator>());
            var body = Child(model.transform, "Body").AddComponent<SkinnedMeshRenderer>();
            body.transform.localScale = new Vector3(1.3f, 1.2f, .7f);
            var bone = Child(body.transform, "Bone").transform;
            var bindPose = bone.worldToLocalMatrix * body.transform.localToWorldMatrix;
            mesh = new Mesh { name = "Regression skin", vertices = new[] { Vector3.right, Vector3.up, Vector3.forward } };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { bindPose };
            var weight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            mesh.boneWeights = new[] { weight, weight, weight };
            mesh.RecalculateNormals();
            body.sharedMesh = mesh;
            body.bones = new[] { bone };
            body.rootBone = bone;
            body.updateWhenOffscreen = true;

            var weapons = host.AddComponent<WeaponManager>();
            Property(player, "WeaponManager", weapons);
            weapons.FireWeapon = Child(host.transform, "Fire weapon");
            weapons.WaterWeapon = Child(host.transform, "Water weapon");
            weapons.FireWeapon.transform.localPosition = Vector3.right;
            weapons.WaterWeapon.transform.localPosition = Vector3.left;
            foreach (var weapon in new[] { weapons.FireWeapon, weapons.WaterWeapon })
            {
                weapon.AddComponent<MeshRenderer>();
                weapon.AddComponent<MeshFilter>().sharedMesh = mesh;
            }

            var forms = new FormManager(player);
            Property(player, "FormManager", forms);
            var machine = new StateMachine();
            Property(player, "StateMachine", machine);
            var neutral = new NeutralState();
            var dash = new PlayerDashState(player);
            host.SetActive(true);
            bone.localPosition = new Vector3(.3f, -.25f, .5f);
            void Tick(float delta) => Invoke(player, "UpdateDashAfterimages", delta);
            object Pool() => Field(player, "_dashAfterimages");

            machine.ChangeState(neutral);
            Tick(.016f);
            Check(Pool() == null, "Idle never allocates the afterimage pool.");

            const string formAssets = "Assets/_Project/Scripts/FormActions/";
            var skill = new PlayerSkillState(player);
            var fire = new FireForm(AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>(formAssets + "FireFormAction.asset"));
            foreach (var form in new BaseForm[]
            {
                fire,
                new EarthForm(AssetDatabase.LoadAssetAtPath<EarthFormActionDataSO>(formAssets + "EarthFormAction.asset")),
                new IronForm(AssetDatabase.LoadAssetAtPath<IronFormActionDataSO>(formAssets + "IronFormAction.asset")),
                new WaterForm(AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>(formAssets + "WaterFormV2.asset")),
                new WoodForm(AssetDatabase.LoadAssetAtPath<WoodFormActionDataSO>(formAssets + "WoodFormAction.asset"))
            })
            {
                forms.ChangeForm(form);
                machine.ChangeState(skill);
                Tick(.016f);
                Tick(.12f);
                Check(Pool() == null, $"{form.Element} E skill does not allocate an afterimage pool.");
                machine.ChangeState(neutral);
            }

            Field(player, "_dashCooldownTimer", 1f);
            machine.ChangeState(dash);
            Tick(.016f);
            Check(dash.IsDashComplete && Pool() == null, "A rejected dash emits no afterimage.");

            machine.ChangeState(neutral);
            Field(player, "_dashCooldownTimer", 0f);
            forms.ChangeForm(new ProbeForm(ElementType.ELEMENT_FIRE));
            machine.ChangeState(dash);
            Tick(0f);
            Check(Pool() == null, "Stopped action time cannot begin a capture.");
            Tick(.016f);
            var pool = Pool();
            Check(pool != null && ActiveCount(pool) == 1, "A successful backstep immediately captures one pose.");
            var first = Latest(pool);
            var pieces = (Array)Field(first, "Pieces");
            var bodyPiece = pieces.GetValue(0);
            var baked = (Mesh)Field(bodyPiece, "Mesh");
            var world = (Matrix4x4)Field(bodyPiece, "WorldMatrix");
            var vertex = baked.vertices[0];
            var expected = (bone.localToWorldMatrix * bindPose).MultiplyPoint3x4(Vector3.right);
            Check((world.MultiplyPoint3x4(vertex) - expected).sqrMagnitude < 1e-6f,
                $"The baked pose applies the skinned render hierarchy's scale once (actual {world.MultiplyPoint3x4(vertex):F5}, expected {expected:F5}).");
            Check((int)Field(first, "PieceCount") == 2 &&
                (Matrix4x4)Field(pieces.GetValue(1), "WorldMatrix") == weapons.FireWeapon.transform.localToWorldMatrix,
                "Fire includes only its equipped weapon.");

            var colorId = Shader.PropertyToID("_BaseColor");
            var properties = (MaterialPropertyBlock)Field(first, "Properties");
            float startingAlpha = properties.GetColor(colorId).a;
            host.transform.position += Vector3.forward;
            bone.localPosition += Vector3.up;
            Tick(.02f);
            float fadingAlpha = properties.GetColor(colorId).a;
            Check(ActiveCount(pool) == 1 && fadingAlpha > 0f && fadingAlpha < startingAlpha,
                "Existing silhouettes fade without spawning before the interval.");
            Check(baked.vertices[0] == vertex && (Matrix4x4)Field(pieces.GetValue(0), "WorldMatrix") == world,
                "An earlier pose stays frozen after the body and bone move.");
            Tick(0f);
            Check(ActiveCount(pool) == 1 && properties.GetColor(colorId).a == fadingAlpha,
                "Stopped action time holds the same pose and opacity.");

            var palettes = new[] {
                (ElementType.ELEMENT_FIRE, 0, 2), (ElementType.ELEMENT_EARTH, 1, 1),
                (ElementType.ELEMENT_GOLD, 2, 1), (ElementType.ELEMENT_WATER, 3, 2), (ElementType.ELEMENT_WOOD, 4, 1) };
            foreach (var (element, paletteIndex, pieceCount) in palettes)
            {
                forms.ChangeForm(new ProbeForm(element));
                Tick(.06f);
                var snapshot = Latest(pool);
                var tint = (Color)Field(snapshot, "Color");
                var palette = FinisherWheelGraphic.ElementColor(paletteIndex);
                Check(Mathf.Abs(tint.r - palette.r) < .001f && Mathf.Abs(tint.g - palette.g) < .001f &&
                    Mathf.Abs(tint.b - palette.b) < .001f && (int)Field(snapshot, "PieceCount") == pieceCount,
                    $"{element} captures its palette color and only its active meshes.");
            }

            var finalSequence = (ulong)Field(Latest(pool), "Sequence");
            Field(dash, "_dashTimer", .2f);
            dash.Update();
            Tick(.06f);
            Check(dash.IsDashComplete && (ulong)Field(Latest(pool), "Sequence") == finalSequence,
                "A completed dash leaves its existing silhouettes fading without capturing another pose.");
            Tick(.23f);
            Check(ActiveCount(pool) == 0, "Every afterimage expires after its lifetime.");

            machine.ChangeState(neutral);
            forms.ChangeForm(fire);
            Field(player, "_dashCooldownTimer", 0f);
            Property(player.Input, "MoveInput", Vector2.up);
            machine.ChangeState(dash);
            Tick(.016f);
            Check(ActiveCount(pool) == 1, "A directional dash immediately captures one pose.");
            Property(player.Input, "MoveInput", Vector2.zero);
            var lastDash = Latest(pool);
            var dashProperties = (MaterialPropertyBlock)Field(lastDash, "Properties");
            float dashAlpha = dashProperties.GetColor(colorId).a;
            ulong dashSequence = (ulong)Field(lastDash, "Sequence");
            machine.ChangeState(skill);
            Tick(.12f);
            float fadedDashAlpha = dashProperties.GetColor(colorId).a;
            Check(ActiveCount(pool) == 1 && (ulong)Field(Latest(pool), "Sequence") == dashSequence
                && fadedDashAlpha > 0f && fadedDashAlpha < dashAlpha,
                "Entering an E skill lets the existing dash silhouette fade without a new capture.");
            Tick(.23f);
            Check(ActiveCount(pool) == 0, "The dash silhouette expires during the E skill.");
            machine.ChangeState(neutral);

            void CheckCleared(Action interrupt, Action restore, string reason)
            {
                Field(player, "_dashCooldownTimer", 0f);
                machine.ChangeState(dash);
                Tick(.016f);
                Check(ActiveCount(pool) == 1, $"Dash captures before {reason}.");
                interrupt();
                try
                {
                    Tick(0f);
                    Check(ActiveCount(pool) == 0, $"{reason} immediately clears the dash silhouettes.");
                }
                finally
                {
                    restore();
                    machine.ChangeState(neutral);
                }
            }
            CheckCleared(() => Property(player, "IsStageTransitioning", true),
                () => Property(player, "IsStageTransitioning", false), "stage transition");
            var fallPhase = typeof(PlayerController).GetField("_fallPhase", Instance);
            var previousFallPhase = fallPhase.GetValue(player);
            CheckCleared(() => fallPhase.SetValue(player, Enum.Parse(fallPhase.FieldType, "DropDelay")),
                () => fallPhase.SetValue(player, previousFallPhase), "fall recovery");
            float hp = player.StatManager.CurrentHp;
            CheckCleared(() => Field(player.StatManager, "_currentHp", 0f),
                () => Field(player.StatManager, "_currentHp", hp), "death");

            for (int i = 0; i < 10; i++) Invoke(pool, "Capture", Color.white, 1f);
            Check(ActiveCount(pool) == 6 && ReferenceEquals(Field(pieces.GetValue(0), "Mesh"), baked),
                "Burst captures reuse baked meshes and cap the number of silhouettes.");
            Invoke(player, "DisposeDashAfterimages");
            Check(Pool() == null && baked == null, "Cleanup releases the generated pose meshes.");
            return $"PASS: dash afterimages ({checks} checks).";
        }
        finally
        {
            if (player != null) Invoke(player, "DisposeDashAfterimages");
            if (host != null) Object.DestroyImmediate(host);
            if (mesh != null) Object.DestroyImmediate(mesh);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static object Latest(object pool)
    {
        object latest = null;
        ulong sequence = 0;
        foreach (var snapshot in (Array)Field(pool, "_snapshots"))
        {
            var candidate = (ulong)Field(snapshot, "Sequence");
            if ((float)Field(snapshot, "Remaining") <= 0f || candidate <= sequence) continue;
            latest = snapshot;
            sequence = candidate;
        }
        return latest ?? throw new InvalidOperationException("Expected an active dash afterimage.");
    }

    private static int ActiveCount(object pool) => (int)pool.GetType().GetProperty("ActiveCount", Instance).GetValue(pool);
    private static object Field(object target, string name) => target.GetType().GetField(name, Instance).GetValue(target);
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name, Instance).SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
}
