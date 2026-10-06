using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Retained cell shell for virtualized multi-column trees. Products own binding and USS styling.
    /// </summary>
    public class CommonMultiColumnTreeCell : VisualElement
    {
        public const string RootClass = "common-multi-column-tree-cell";
        public const string IconClass = "common-multi-column-tree-cell__icon";
        public const string LabelClass = "common-multi-column-tree-cell__label";
        public const string ActionClass = "common-multi-column-tree-cell__action";
        public const string AccessoryClass = "common-multi-column-tree-cell__accessory";

        public Image Icon { get; }
        public Label Label { get; }
        public Button Action { get; }
        public VisualElement Accessory { get; }
        public int ContentStateHash { get; set; } = int.MinValue;

        public CommonMultiColumnTreeCell()
        {
            AddToClassList(RootClass);

            Icon = new Image();
            Icon.AddToClassList(IconClass);
            Add(Icon);

            Label = new Label();
            Label.AddToClassList(LabelClass);
            Add(Label);

            Action = new Button();
            Action.AddToClassList(ActionClass);
            Add(Action);

            Accessory = new VisualElement();
            Accessory.AddToClassList(AccessoryClass);
            Add(Accessory);
        }

        public void ResetContent()
        {
            Icon.image = null;
            Icon.tooltip = string.Empty;
            Label.text = string.Empty;
            Label.tooltip = string.Empty;
            Action.text = string.Empty;
            Action.tooltip = string.Empty;
            Accessory.Clear();
            ContentStateHash = int.MinValue;
        }
    }
}
