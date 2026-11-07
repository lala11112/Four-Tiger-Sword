using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Authors the reusable Hub UI and connects the existing scenes without replacing them.</summary>
public static class HubUpgradeSetup
{
    public const string HubPath = "Assets/_Project/Scenes/Hub Scene.unity";
    public const string PrefabPath = "Assets/_Project/Prefabs/UI/HubUpgradeMenu.prefab";
    public const string SettingsPath = "Assets/_Project/Resources/PlayerUpgradeSettings.asset";
    private static readonly Color Ink = new Color32(228, 236, 243, 255);
    private static readonly Color Muted = new Color32(143, 163, 183, 255);
    private static readonly Color Red = new Color32(215, 51, 79, 255);
    private static TMP_FontAsset _font;
    private static Material _material;

    [MenuItem("Tools/Studio Baekho/Progression/Set Up Hub Upgrades")]
    public static string Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Set up Hub in Edit Mode.");
        var settings = AssetDatabase.LoadAssetAtPath<PlayerUpgradeSettingsSO>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PlayerUpgradeSettingsSO>();
            settings.basePlayerStats = AssetDatabase.LoadAssetAtPath<PlayerStatData>("Assets/_Project/Scripts/NewPlayer/PlayerStatData.asset");
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        _font = BoundaryZeroTypography.GetFont();
        _material = BoundaryZeroTypography.GetMaterial();
        BuildPrefab();
        var hub = SceneManager.GetSceneByPath(HubPath);
        if (!hub.isLoaded) hub = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Additive);
        var view = hub.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HubUpgradeView>(true)).FirstOrDefault();
        if (view == null) view = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), hub)).GetComponent<HubUpgradeView>();
        var controller = hub.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HubSceneController>(true)).FirstOrDefault();
        if (controller == null)
        {
            var host = new GameObject("HubController");
            SceneManager.MoveGameObjectToScene(host, hub);
            controller = host.AddComponent<HubSceneController>();
        }
        var overlay = hub.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BoundaryZeroRunOverlay>(true)).FirstOrDefault();
        if (overlay == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab");
            if (prefab == null) throw new InvalidOperationException("Run loading overlay is missing.");
            overlay = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, hub)).GetComponent<BoundaryZeroRunOverlay>();
        }
        overlay.Hide();
        var frame = view.transform.Find("Content");
        controller.Configure(view, frame.Find("Actions/StartRun").GetComponent<UnityEngine.UI.Button>(),
            frame.Find("Actions/Title").GetComponent<UnityEngine.UI.Button>(),
            frame.Find("Actions/RetryLoad").GetComponent<UnityEngine.UI.Button>(),
            frame.Find("Status").GetComponent<TMP_Text>(), overlay);
        EditorUtility.SetDirty(controller);
        var eventSystem = hub.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).FirstOrDefault();
        if (eventSystem == null)
        {
            var host = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(host, hub);
            host.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            eventSystem = host.GetComponent<EventSystem>();
        }
        eventSystem.firstSelectedGameObject = frame.Find("Actions/StartRun").gameObject;
        EditorUtility.SetDirty(eventSystem);
        EditorSceneManager.MarkSceneDirty(hub);
        if (!EditorSceneManager.SaveScene(hub)) throw new InvalidOperationException("Could not save Hub scene.");

        const string gamePath = "Assets/_Project/Scenes/Game Scene.unity";
        var game = SceneManager.GetSceneByPath(gamePath);
        bool opened = !game.isLoaded;
        if (opened) game = EditorSceneManager.OpenScene(gamePath, OpenSceneMode.Additive);
        try
        {
            var player = game.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).Single();
            if (player.GetComponent<PlayerUpgradeBinding>() == null) player.gameObject.AddComponent<PlayerUpgradeBinding>();
            var stats = new SerializedObject(player.GetComponent<PlayerStatManager>());
            if (stats.FindProperty("baseData").objectReferenceValue != settings.basePlayerStats)
                throw new InvalidOperationException("Hub preview and game player must share the same base stat asset.");
            EditorSceneManager.MarkSceneDirty(game);
            if (!EditorSceneManager.SaveScene(game)) throw new InvalidOperationException("Could not save player upgrade binding.");
        }
        finally { if (opened) EditorSceneManager.CloseScene(game, true); }
        var scenes = EditorBuildSettings.scenes.ToList();
        int hubIndex = scenes.FindIndex(s => s.path == HubPath);
        if (hubIndex < 0) scenes.Insert(Mathf.Min(1, scenes.Count), new EditorBuildSettingsScene(HubPath, true));
        else scenes[hubIndex] = new EditorBuildSettingsScene(HubPath, true);
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssetIfDirty(settings);
        return "Hub menu, upgrade settings, player binding and build scenes connected.";
    }

    private static void BuildPrefab()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("HubUpgradeMenu", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster), typeof(HubUpgradeView));
            SceneManager.MoveGameObjectToScene(root, scene);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var backdrop = Rect("Backdrop", root.transform);
            Stretch(backdrop);
            backdrop.gameObject.AddComponent<BoundaryZeroMainBackdrop>().raycastTarget = false;
            var frame = Rect("Content", root.transform);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
            frame.gameObject.AddComponent<BoundaryZeroWindowFit>().Configure(new Vector2(1680, 940), 1f, 40f);
            Label("Eyebrow", frame, "BOUNDARY ZERO   /   BASE CAMP", 0, 0, 1000, 40, 17, Muted);
            Label("Heading", frame, "다음 탐사를 위한 준비", 0, 58, 1100, 80, 52, Ink);
            Label("Subtitle", frame, "강화 재화로 능력을 성장시키세요. 강화는 탐사가 끝나도 유지됩니다.", 0, 147, 1100, 50, 23, Muted);
            Image("TitleRule", frame, 0, 210, 1680, 1, new Color32(61, 79, 96, 255));
            Image("Accent", frame, 0, 208, 72, 4, Red);
            Picture("Crystal", frame, "stat_legacycurrency.png", 1320, 64, 60, 60);
            var balance = Label("Balance", frame, "강화석   0", 1400, 60, 280, 64, 29, new Color32(224, 206, 162, 255));
            balance.alignment = TextAlignmentOptions.MidlineRight;
            Label("BalanceHint", frame, "영구 성장 재화", 1370, 129, 310, 32, 17, Muted).alignment = TextAlignmentOptions.MidlineRight;

            var row = Place(Rect("Cards", frame), 0, 260, 1680, 440);
            var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 24;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            var cards = new[] {
                BuildCard(row, "Health", "최대 체력", "VITALITY", "stat_health.png", StatType.ST_HP, new Color32(217, 94, 115, 255), "5,000  →  5,250"),
                BuildCard(row, "Attack", "공격력", "POWER", "stat_attack.png", StatType.ST_ATK, new Color32(230, 187, 113, 255), "100  →  105"),
                BuildCard(row, "Defense", "방어력", "RESILIENCE", "element_metal.png", StatType.ST_DEF, new Color32(128, 199, 211, 255), "50  →  52.5")
            };
            var message = Label("Message", frame, "모든 강화는 확정 적용되며 자동으로 저장됩니다.", 0, 725, 1680, 40, 21, Muted);
            Label("GuideHeading", frame, "재화 획득", 0, 790, 180, 34, 19, Ink);
            var guide = Label("RewardGuide", frame, "일반 구역 10  /  보스 구역 30  /  사망 시 전투 방당 2, 최대 10", 185, 790, 1495, 34, 19, Muted);
            var actions = Place(Rect("Actions", frame), 0, 858, 1680, 70);
            Button("Title", actions, "타이틀로", 0, 0, 235, 66, new Color32(37, 52, 68, 255));
            var retry = Button("RetryLoad", actions, "다시 불러오기", 265, 0, 275, 66, new Color32(73, 55, 64, 255));
            retry.gameObject.SetActive(false);
            Button("StartRun", actions, "탐사 시작   →", 1290, 0, 390, 66, Red);
            var status = Label("Status", frame, "준비가 끝나면 새로운 탐사를 시작하세요.", 560, 872, 690, 40, 19, Muted);
            root.GetComponent<HubUpgradeView>().Configure(frame.gameObject, balance, message, guide, cards);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static HubUpgradeView.Card BuildCard(Transform parent, string name, string title, string subtitle,
        string icon, StatType stat, Color accent, string example)
    {
        var card = Rect(name, parent);
        card.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
        var background = card.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color32(21, 33, 47, 245);
        background.raycastTarget = false;
        var edge = Image("Accent", card, 0, 0, 544, 3, accent);
        edge.rectTransform.anchorMax = new Vector2(1, 1); edge.rectTransform.sizeDelta = new Vector2(0, 3);
        Picture("Icon", card, icon, 30, 32, 68, 68);
        Label("Name", card, title, 118, 34, 330, 43, 31, Ink);
        Label("Subtitle", card, subtitle, 120, 80, 330, 29, 15, Muted);
        var level = Label("Level", card, "Lv. 00 <color=#6E8398>/ 20</color>", 30, 132, 460, 40, 21, accent);
        var value = Label("Value", card, example, 30, 180, 484, 62, 35, Ink);
        var gain = Label("Gain", card, "단계마다 기본 능력치 +5%", 30, 250, 460, 36, 20, Muted);
        var track = Place(Rect("Steps", card), 30, 301, 484, 7);
        var horizontal = track.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        horizontal.spacing = 5; horizontal.childControlWidth = horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = horizontal.childForceExpandHeight = true;
        var steps = new UnityEngine.UI.Image[20];
        for (int i = 0; i < steps.Length; i++)
            steps[i] = Image("Step" + (i + 1), track, 0, 0, 1, 7, new Color32(43, 59, 75, 255));
        var price = Label("Price", card, "강화석  10", 30, 340, 210, 62, 20, Ink);
        var buy = Button("Purchase", card, "강화석 부족", 272, 340, 242, 62, new Color32(49, 71, 89, 255));
        buy.interactable = false;
        return new HubUpgradeView.Card { stat = stat, level = level, value = value, gain = gain, price = price,
            purchase = buy, actionLabel = buy.GetComponentInChildren<TMP_Text>(), steps = steps, accent = accent };
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }
    private static RectTransform Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static UnityEngine.UI.Image Image(string name, Transform parent, float x, float y, float w, float h, Color tint)
    {
        var image = Place(Rect(name, parent), x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = tint; image.raycastTarget = false; return image;
    }
    private static void Picture(string name, Transform parent, string file, float x, float y, float w, float h)
    {
        var image = Image(name, parent, x, y, w, h, Color.white);
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprites/UI/BoundaryZERO/Icons/" + file);
        image.preserveAspect = true;
    }
    private static TMP_Text Label(string name, Transform parent, string text, float x, float y, float w, float h, float size, Color tint)
    {
        var label = Place(Rect(name, parent), x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = _font; label.fontSharedMaterial = _material; label.fontSize = size; label.text = text;
        label.color = tint; label.raycastTarget = false; label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Overflow;
        return label;
    }
    private static UnityEngine.UI.Button Button(string name, Transform parent, string text, float x, float y, float w, float h, Color tint)
    {
        var image = Image(name, parent, x, y, w, h, tint); image.raycastTarget = true;
        var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f); colors.selectedColor = new Color(1.45f, 1.45f, 1.45f);
        colors.pressedColor = new Color(.7f, .7f, .7f); colors.disabledColor = new Color(.43f, .48f, .55f, .8f);
        button.colors = colors;
        var label = Label("Label", image.transform, text, 0, 0, w, h, 23, Ink);
        Stretch(label.rectTransform); label.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
