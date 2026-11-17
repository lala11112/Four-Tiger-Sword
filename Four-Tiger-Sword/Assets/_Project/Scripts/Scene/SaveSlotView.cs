using System;
using System.Collections.Generic;
using BoundaryZeroUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

[DefaultExecutionOrder(-50), DisallowMultipleComponent]
public sealed class SaveSlotView : MonoBehaviour
{
    private static readonly Color ErrorInk = new Color32(255, 149, 140, 255);
    private static readonly Color Border = new Color32(94, 122, 146, 170);

    private enum Page { Slots, Create, Delete }

    private sealed class Row
    {
        public SaveSlotInfo Slot;
        public TMP_Text Name, Balance, PrimaryLabel;
        public UnityEngine.UI.Button Primary, Delete;
    }

    private readonly Row[] _rows = new Row[SaveSlotService.MaxSlots];
    private readonly List<UnityEngine.UI.Selectable> _tabOrder = new();
    private RectTransform _list, _dialog, _nameEditor;
    private TMP_Text _heading, _listError, _dialogHeading, _dialogBody, _dialogError, _confirmLabel;
    private Page _page;
    private string _deleteId;
    private int _selectedNumber;
    private bool _busy;

    public bool IsOpen => gameObject.activeInHierarchy;
    public UnityEngine.UI.Button BackButton { get; private set; }
    public UnityEngine.UI.Button ConfirmButton { get; private set; }
    public UnityEngine.UI.Button CancelButton { get; private set; }
    public TMP_InputField NameInput { get; private set; }
    public UnityEngine.UI.Button GetPrimaryButton(int number) => GetRow(number)?.Primary;
    public UnityEngine.UI.Button GetDeleteButton(int number) => GetRow(number)?.Delete;

    public event Action<string> SlotChosen;
    public event Action<int, string> CreateRequested;
    public event Action<string> DeleteRequested;
    public event Action Closed;

    public static SaveSlotView Ensure(TitleManager owner)
    {
        var existing = owner.transform.Find("SaveSlotCanvas");
        if (existing != null && existing.TryGetComponent(out SaveSlotView view)) return view;

        var root = new GameObject("SaveSlotCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        root.transform.SetParent(owner.transform, false);
        Stretch((RectTransform)root.transform);
        view = root.AddComponent<SaveSlotView>();
        view.Build();
        view.Hide();
        return view;
    }

    public void Show(IReadOnlyList<SaveSlotInfo> slots, bool replay)
    {
        _busy = false;
        _selectedNumber = 1;
        _heading.text = replay ? "튜토리얼 저장 슬롯" : "저장 슬롯";
        SetPage(Page.Slots);
        gameObject.SetActive(true);
        Refresh(slots);
        SelectPreferred();
    }

    public void Refresh(IReadOnlyList<SaveSlotInfo> slots)
    {
        foreach (var row in _rows) row.Slot = null;
        if (slots != null)
            foreach (var slot in slots)
                if (slot != null && GetRow(slot.Number) is { } row) row.Slot = slot;

        foreach (var row in _rows) RefreshRow(row);

        var selected = GetRow(_selectedNumber)?.Slot;
        if ((_page == Page.Create && selected != null) || (_page == Page.Delete && (selected == null || selected.Id != _deleteId)))
            SetPage(Page.Slots);
        else if (_page == Page.Delete)
            _dialogBody.text = DeleteMessage(selected);
        RefreshControls();
        PreserveFocus();
    }

    public void Hide()
    {
        ClearSelection();
        _busy = false;
        SetPage(Page.Slots);
        gameObject.SetActive(false);
    }

    public void SetBusy(bool busy)
    {
        if (_busy == busy) return;
        _busy = busy;
        if (busy) ClearSelection();
        RefreshControls();
        if (!busy) PreserveFocus();
    }

    public void ShowError(string error)
    {
        _listError.text = _dialogError.text = error ?? string.Empty;
    }

    private void Build()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 150;
        var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1440);
        scaler.matchWidthOrHeight = .5f;

        var shade = UIFactory.Box(transform, "Shade", 0, 0, 1, 1, new Color32(4, 9, 18, 188), true).rectTransform;
        Stretch(shade);

        _list = BuildWindow("SlotList", 1152f, 704f);
        _heading = Label(_list, "Heading", "저장 슬롯", 48, 37, 845, 54, 44, UIFactory.Ivory);
        BackButton = Button(_list, "Back", "X", 1047, 37, 57, 57, Back);
        UIFactory.Line(_list, 48, 106, 1056, 1, Border);
        Label(_list, "SlotHeading", "슬롯 / 이름", 65, 119, 530, 36, 22, UIFactory.Muted);
        Label(_list, "BalanceHeading", "보유 강화석", 592, 119, 157, 36, 22, UIFactory.Muted, TextAlignmentOptions.MidlineRight);
        for (int i = 0; i < _rows.Length; i++) _rows[i] = BuildRow(i + 1, 160 + i * 91);
        _listError = Label(_list, "Error", "", 50, 630, 1052, 50, 24, ErrorInk);

        _dialog = BuildWindow("SlotDialog", 816f, 466f);
        _dialogHeading = Label(_dialog, "Heading", "", 43, 32, 730, 58, 43, UIFactory.Ivory);
        UIFactory.Line(_dialog, 44, 105, 728, 1, Border);
        _dialogBody = Label(_dialog, "Description", "", 44, 129, 728, 105, 27, UIFactory.Ivory);
        _dialogBody.enableAutoSizing = true;
        _dialogBody.fontSizeMin = 20;
        _dialogBody.fontSizeMax = 27;
        _nameEditor = BuildNameEditor(_dialog);
        _dialogError = Label(_dialog, "Error", "", 44, 316, 728, 46, 22, ErrorInk);
        CancelButton = Button(_dialog, "Cancel", "취소", 343, 375, 204, 61, Back);
        ConfirmButton = Button(_dialog, "Confirm", "확인", 569, 375, 203, 61, Confirm, true);
        _confirmLabel = ConfirmButton.GetComponentInChildren<TMP_Text>();

        foreach (var graphic in GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            if (graphic.GetComponent<CanvasRenderer>() == null) graphic.gameObject.AddComponent<CanvasRenderer>();
            graphic.SetAllDirty();
        }
        UIButtonMotion.Install(_list.gameObject);
        UIButtonMotion.Install(_dialog.gameObject);
    }

    private RectTransform BuildWindow(string objectName, float width, float height)
    {
        var frame = UIFactory.Node(transform, objectName, 0, 0, width, height);
        frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
        frame.anchoredPosition = Vector2.zero;
        frame.gameObject.AddComponent<BoundaryZeroWindowFit>().Configure(new Vector2(width, height), 1f);
        DesignArt.Surface(frame, 0, 0, width, height, new Color32(27, 39, 55, 255), new Color32(9, 16, 27, 255));
        UIFactory.Border(frame, 0, 0, width, height, Border);
        UIFactory.Line(frame, 48, 0, 95, 3, UIFactory.Red);
        return frame;
    }

    private Row BuildRow(int number, float y)
    {
        var root = UIFactory.Node(_list, "SaveSlotRow" + number, 48, y, 1056, 81);
        UIFactory.Box(root, "RowSurface", 0, 0, 1056, 81, new Color32(25, 39, 57, 238));
        UIFactory.Line(root, 0, 80, 1056, 1, Border);
        Label(root, "Number", number.ToString("00"), 17, 1, 53, 79, 25, UIFactory.Cyan, TextAlignmentOptions.MidlineLeft);
        var name = SingleLine(root, "Name", "", 80, 1, 440, 79, 27, UIFactory.Ivory, TextAlignmentOptions.MidlineLeft);
        name.enableAutoSizing = true;
        name.fontSizeMin = 19;
        name.fontSizeMax = 27;
        var balance = SingleLine(root, "Balance", "", 542, 1, 159, 79, 23, UIFactory.Ivory, TextAlignmentOptions.MidlineRight);
        var primary = Button(root, "LoadOrCreate", "불러오기", 774, 14, 150, 53, () => Choose(number), true);
        var delete = Button(root, "Delete", "삭제", 937, 14, 102, 53, () => AskDelete(number));
        return new Row { Name = name, Balance = balance, Primary = primary, Delete = delete,
            PrimaryLabel = primary.GetComponentInChildren<TMP_Text>() };
    }

    private RectTransform BuildNameEditor(RectTransform parent)
    {
        var root = UIFactory.Node(parent, "NameEditor", 44, 227, 728, 76);
        UIFactory.Box(root, "InputSurface", 0, 0, 728, 76, new Color32(13, 24, 40, 255));
        UIFactory.Border(root, 0, 0, 728, 76, Border);
        var inputRoot = UIFactory.Node(root, "NameInput", 0, 0, 728, 76);
        var viewport = UIFactory.Node(inputRoot, "TextViewport", 18, 7, 692, 62);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        // Geometry alignment centers TMP's caret on the top edge of this top-pivoted text rect, clipping it.
        var editable = SingleLine(viewport, "Value", "", 0, 0, 692, 62, 34, UIFactory.Ivory, TextAlignmentOptions.Left);
        var placeholder = Label(viewport, "Placeholder", "슬롯 이름 (선택)", 14, 0, 678, 62, 27, UIFactory.Muted, TextAlignmentOptions.Left);

        // The field's own selectable surface receives the pointer events used for editing.
        var hit = UIFactory.Box(inputRoot, "InputHit", 0, 0, 728, 76, new Color(1f, 1f, 1f, .001f), true);
        hit.transform.SetAsFirstSibling();
        NameInput = inputRoot.gameObject.AddComponent<TMP_InputField>();
        NameInput.targetGraphic = hit;
        NameInput.textViewport = viewport;
        NameInput.textComponent = editable;
        NameInput.placeholder = placeholder;
        NameInput.lineType = TMP_InputField.LineType.SingleLine;
        NameInput.characterLimit = SaveSlotService.MaxNameLength;
        NameInput.richText = false;
        NameInput.caretWidth = 4;
        NameInput.selectionColor = new Color(.2f, .53f, .69f, .7f);
        NameInput.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        NameInput.onSubmit.AddListener(_ => Select(ConfirmButton));
        return root;
    }

    private void RefreshRow(Row row)
    {
        var slot = row.Slot;
        row.Name.text = slot == null ? "빈 슬롯" : DisplayName(slot);
        row.Balance.text = slot?.IsReadable == true ? slot.Balance.ToString("N0") : "—";
        row.PrimaryLabel.text = slot == null ? "새로 만들기" : slot.IsReadable ? "불러오기" : "읽기 실패";
        row.PrimaryLabel.color = slot != null && !slot.IsReadable ? UIFactory.Muted : UIFactory.Ivory;
        row.Delete.gameObject.SetActive(slot != null);
    }

    private void Choose(int number)
    {
        var row = GetRow(number);
        if (!IsOpen || _busy || _page != Page.Slots || row == null) return;
        _selectedNumber = number;
        ShowError(null);
        if (row.Slot == null)
        {
            _dialogHeading.text = "새 저장 슬롯";
            _dialogBody.text = $"슬롯 {number}에 새로 저장합니다. 이름을 비워 두면 기본 이름을 사용합니다.";
            NameInput.SetTextWithoutNotify(string.Empty);
            _confirmLabel.text = "만들고 시작";
            SetPage(Page.Create);
            Select(NameInput);
        }
        else if (row.Slot.IsReadable) SlotChosen?.Invoke(row.Slot.Id);
        else ShowError(row.Slot.Error ?? "저장 슬롯을 읽지 못했습니다.");
    }

    private void AskDelete(int number)
    {
        var slot = GetRow(number)?.Slot;
        if (!IsOpen || _busy || _page != Page.Slots || slot == null) return;
        _selectedNumber = number;
        _deleteId = slot.Id;
        ShowError(null);
        _dialogHeading.text = "저장 슬롯 삭제";
        _dialogBody.text = DeleteMessage(slot);
        _confirmLabel.text = "삭제";
        SetPage(Page.Delete);
        Select(CancelButton);
    }

    private void Confirm()
    {
        if (!IsOpen || _busy || _page == Page.Slots) return;
        var slot = GetRow(_selectedNumber)?.Slot;
        ShowError(null);
        if (_page == Page.Create && slot == null)
        {
            string name = NameInput.text.Trim();
            if (name.Length > SaveSlotService.MaxNameLength)
            { ShowError($"이름은 {SaveSlotService.MaxNameLength}자까지 입력할 수 있습니다."); return; }
            CreateRequested?.Invoke(_selectedNumber, name);
        }
        else if (_page == Page.Delete && slot != null && slot.Id == _deleteId)
            DeleteRequested?.Invoke(_deleteId);
    }

    private void Back()
    {
        if (!IsOpen || _busy) return;
        if (_page == Page.Slots)
        {
            Hide();
            Closed?.Invoke();
            return;
        }
        ShowError(null);
        SetPage(Page.Slots);
        SelectPreferred();
    }

    private void SetPage(Page page)
    {
        if (page != Page.Create) NameInput.DeactivateInputField();
        _page = page;
        if (page != Page.Delete) _deleteId = null;
        _list.gameObject.SetActive(page == Page.Slots);
        _dialog.gameObject.SetActive(page != Page.Slots);
        _nameEditor.gameObject.SetActive(page == Page.Create);
        RefreshControls();
    }

    private void Update()
    {
        if (_busy) return;
        var keyboard = Keyboard.current;
        var input = EventSystem.current?.currentInputModule as InputSystemUIInputModule;
        if (keyboard?.escapeKey.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true
            || input?.cancel?.action?.WasPressedThisFrame() == true)
        {
            Back();
            return;
        }
        if (keyboard?.tabKey.wasPressedThisFrame != true || _tabOrder.Count == 0) return;
        var selected = EventSystem.current?.currentSelectedGameObject?.GetComponent<UnityEngine.UI.Selectable>();
        int current = _tabOrder.IndexOf(selected);
        int direction = keyboard.shiftKey.isPressed ? -1 : 1;
        int next = current < 0 ? (direction > 0 ? 0 : _tabOrder.Count - 1)
            : (current + direction + _tabOrder.Count) % _tabOrder.Count;
        Select(_tabOrder[next]);
    }

    private void LateUpdate()
    {
        if (_busy) return;
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected == null) SelectPreferred();
        else if (_page == Page.Create && selected == NameInput.gameObject && Gamepad.current is { } pad
            && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame))
            Select(ConfirmButton);
    }

    private void RefreshControls()
    {
        BackButton.interactable = CancelButton.interactable = ConfirmButton.interactable = NameInput.interactable = !_busy;
        foreach (var row in _rows)
        {
            row.Primary.interactable = !_busy && (row.Slot == null || row.Slot.IsReadable);
            row.Delete.interactable = !_busy && !string.IsNullOrEmpty(row.Slot?.Id);
        }
        _tabOrder.Clear();
        if (_busy) return;
        if (_page == Page.Slots) ConfigureSlotNavigation();
        else
        {
            var field = _page == Page.Create ? NameInput : null;
            SetNavigation(CancelButton, field, null, null, ConfirmButton);
            SetNavigation(ConfirmButton, field, null, CancelButton, null);
            if (field != null) _tabOrder.Add(field);
            _tabOrder.Add(CancelButton);
            _tabOrder.Add(ConfirmButton);
        }
    }

    private void ConfigureSlotNavigation()
    {
        for (int i = 0; i < _rows.Length; i++)
        {
            var row = _rows[i];
            SetNavigation(row.Primary, FindRowAction(i - 1, -1, false), FindRowAction(i + 1, 1, false),
                null, row.Delete.interactable ? row.Delete : null);
            SetNavigation(row.Delete, FindRowAction(i - 1, -1, true), FindRowAction(i + 1, 1, true),
                row.Primary.interactable ? row.Primary : null, null);
            if (row.Primary.interactable) _tabOrder.Add(row.Primary);
            if (row.Delete.interactable) _tabOrder.Add(row.Delete);
        }
        SetNavigation(BackButton, FindRowAction(_rows.Length - 1, -1, false), FindRowAction(0, 1, false), null, null);
        _tabOrder.Add(BackButton);
    }

    private UnityEngine.UI.Selectable FindRowAction(int index, int direction, bool preferDelete)
    {
        while (index >= 0 && index < _rows.Length)
        {
            var row = _rows[index];
            if (preferDelete && row.Delete.interactable) return row.Delete;
            if (row.Primary.interactable) return row.Primary;
            if (row.Delete.interactable) return row.Delete;
            index += direction;
        }
        return BackButton;
    }

    private void PreserveFocus()
    {
        if (!IsOpen || _busy) return;
        var current = EventSystem.current?.currentSelectedGameObject?.GetComponent<UnityEngine.UI.Selectable>();
        if (!_tabOrder.Contains(current)) SelectPreferred();
    }

    private void SelectPreferred()
    {
        if (_busy) return;
        switch (_page)
        {
            case Page.Create: Select(ConfirmButton); break;
            case Page.Delete: Select(CancelButton); break;
            default: Select(FindRowAction(Mathf.Clamp(_selectedNumber - 1, 0, _rows.Length - 1), 1, false)); break;
        }
    }

    private void ClearSelection()
    {
        var events = EventSystem.current;
        var current = events != null ? events.currentSelectedGameObject : null;
        if (current != null && current.transform.IsChildOf(transform)) events.SetSelectedGameObject(null);
    }

    private Row GetRow(int number) => number > 0 && number <= _rows.Length ? _rows[number - 1] : null;

    private static string DisplayName(SaveSlotInfo slot) => string.IsNullOrWhiteSpace(slot.Name) ? $"슬롯 {slot.Number}" : slot.Name;

    private static string DeleteMessage(SaveSlotInfo slot)
        => $"‘{DisplayName(slot)}’을(를) 삭제할까요?\n튜토리얼, 강화·각인, 강화석과 다음 탐사 보급품이 함께 삭제됩니다.";

    private static TMP_Text Label(Transform parent, string objectName, string text, float x, float y, float w, float h,
        float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var label = UIFactory.Label(parent, text, x, y, w, h, size, color, alignment);
        label.gameObject.name = objectName;
        label.richText = false;
        return label;
    }

    private static TMP_Text SingleLine(Transform parent, string objectName, string text, float x, float y, float w, float h,
        float size, Color color, TextAlignmentOptions alignment)
    {
        var label = Label(parent, objectName, text, x, y, w, h, size, color, alignment);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private static UnityEngine.UI.Button Button(Transform parent, string objectName, string title, float x, float y,
        float width, float height, Action callback, bool primary = false)
    {
        var button = UIFactory.Button(parent, title, x, y, width, height, callback, primary);
        button.gameObject.name = objectName;
        return button;
    }

    private static void Select(UnityEngine.UI.Selectable target)
    {
        if (target != null && target.IsActive() && target.IsInteractable() && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void SetNavigation(UnityEngine.UI.Selectable control, UnityEngine.UI.Selectable up,
        UnityEngine.UI.Selectable down, UnityEngine.UI.Selectable left, UnityEngine.UI.Selectable right)
    {
        control.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
            selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right };
    }
}
