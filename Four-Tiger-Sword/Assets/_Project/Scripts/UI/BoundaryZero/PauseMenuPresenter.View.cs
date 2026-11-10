using BoundaryZeroUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class PauseMenuPresenter
{
    private Canvas _canvas;
    private GameObject _menuButtons, _confirmation;
    private TMP_Text _heading, _message, _error;
    private UnityEngine.UI.Button _continueButton, _titleButton, _quitButton, _cancelButton, _confirmButton;
    private UIModalMotion _motion;

    private void BuildView()
    {
        if (_canvas != null) return;
        var root = new GameObject("PauseMenuCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 220;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1440);
        scaler.matchWidthOrHeight = .5f;

        // Cover the actual canvas, including the sides of ultrawide displays.
        var shade = new GameObject("Shade", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        shade.transform.SetParent(root.transform, false);
        var shadeRect = (RectTransform)shade.transform;
        shadeRect.anchorMin = Vector2.zero;
        shadeRect.anchorMax = Vector2.one;
        shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
        shade.GetComponent<UnityEngine.UI.Image>().color = new Color(.015f, .025f, .045f, .78f);

        var frame = new GameObject("Window", typeof(RectTransform));
        frame.transform.SetParent(root.transform, false);
        var rect = (RectTransform)frame.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(920, 690);
        // AddComponent invokes OnEnable, whose default design size can resize the rect.
        frame.AddComponent<BoundaryZeroWindowFit>().Configure(new Vector2(920, 690), 1f);
        var visual = UIFactory.Node(rect, "Visual", 0, 0, 920, 690);
        DesignArt.Surface(visual, 0, 0, 920, 690, new Color32(24, 32, 44, 255), new Color32(10, 16, 27, 255));
        UIFactory.Border(visual, 0, 0, 920, 690, new Color(.64f, .71f, .79f, .32f));
        UIFactory.Line(visual, 48, 48, 52, 3, UIFactory.Red);
        UIFactory.Label(visual, "BOUNDARY ZERO", 118, 32, 650, 36, 19, UIFactory.Muted);
        _heading = UIFactory.Label(visual, "일시 정지", 48, 95, 824, 72, 44, UIFactory.Ivory, TextAlignmentOptions.Center);
        UIFactory.Line(visual, 48, 182, 824, 1);
        UIFactory.Label(visual, "ESC  ·  돌아가기", 48, 624, 824, 32, 20, UIFactory.Muted, TextAlignmentOptions.Center);

        var menu = UIFactory.Node(visual, "Menu", 0, 210, 920, 385);
        _menuButtons = menu.gameObject;
        _continueButton = UIFactory.Button(menu, "계속하기", 190, 0, 540, 86, Resume, true);
        _titleButton = UIFactory.Button(menu, "타이틀로", 190, 114, 540, 86, RequestTitle);
        _quitButton = UIFactory.Button(menu, "게임 종료", 190, 228, 540, 86, RequestQuit);
        SetNavigation(_continueButton, _titleButton, _quitButton);

        var confirmation = UIFactory.Node(visual, "Confirmation", 0, 205, 920, 395);
        _confirmation = confirmation.gameObject;
        _message = UIFactory.Label(confirmation, "", 65, 4, 790, 135, 27, UIFactory.Ivory, TextAlignmentOptions.Center);
        _error = UIFactory.Label(confirmation, "", 65, 151, 790, 80, 22, new Color(1f, .57f, .53f), TextAlignmentOptions.Center);
        _cancelButton = UIFactory.Button(confirmation, "취소", 130, 275, 310, 80, CancelConfirmation);
        _confirmButton = UIFactory.Button(confirmation, "확인", 480, 275, 310, 80, ConfirmExit, true);
        SetNavigation(_cancelButton, _confirmButton);
        foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            // The shared procedural frame graphic does not require a CanvasRenderer itself.
            if (graphic.GetComponent<CanvasRenderer>() == null) graphic.gameObject.AddComponent<CanvasRenderer>();
            graphic.SetAllDirty();
        }
        UIButtonMotion.Install(frame);
        _motion = UIModalMotion.Ensure(root, visual);
        root.SetActive(false);
    }

    private void ShowMenu()
    {
        _heading.text = "일시 정지";
        _menuButtons.SetActive(true);
        _confirmation.SetActive(false);
        SetButtonsInteractable(true);
    }

    private void ShowConfirmation()
    {
        _heading.text = _choice == ExitChoice.Title ? "타이틀로 이동" : "게임 종료";
        _message.text = _run != null
            ? "현재 탐사 진행 상황은 저장되지 않습니다.\n이미 획득한 영구 보상은 유지됩니다."
            : _choice == ExitChoice.Title ? "타이틀 화면으로 돌아갈까요?" : "게임을 종료할까요?";
        _error.text = LastError ?? string.Empty;
        _menuButtons.SetActive(false);
        _confirmation.SetActive(true);
        SetButtonsInteractable(!IsExiting);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (_continueButton == null) return;
        _continueButton.interactable = _titleButton.interactable = _quitButton.interactable = interactable;
        _cancelButton.interactable = _confirmButton.interactable = interactable;
    }

    private static void Select(UnityEngine.UI.Button button)
    {
        if (EventSystem.current != null && button != null && button.isActiveAndEnabled)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    private static void SetNavigation(params UnityEngine.UI.Button[] buttons)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            var previous = buttons[(i + buttons.Length - 1) % buttons.Length];
            var next = buttons[(i + 1) % buttons.Length];
            buttons[i].navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = previous, selectOnLeft = previous,
                selectOnDown = next, selectOnRight = next
            };
        }
    }
}
