using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonEmptyState : VisualElement
    {
        public sealed class EmptyStateClasses
        {
            public string RootClass;
            public string IconClass;
            public string TitleClass;
            public string DetailClass;
            public string ActionsClass;
        }

        public Image Icon { get; }
        public Label Title { get; }
        public Label Detail { get; }
        public VisualElement Actions { get; }

        public CommonEmptyState(EmptyStateClasses classes = null)
        {
            CommonUITK.AddClasses(this, classes?.RootClass);

            Icon = new Image
            {
                scaleMode = ScaleMode.ScaleToFit
            };
            CommonUITK.AddClasses(Icon, classes?.IconClass);
            Add(Icon);

            Title = CommonUITK.CreateLabel(string.Empty, classes?.TitleClass);
            Add(Title);

            Detail = CommonUITK.CreateLabel(string.Empty, classes?.DetailClass);
            Add(Detail);

            Actions = CommonUITK.CreateContainer(classes?.ActionsClass);
            Add(Actions);

            SetContent(null, null);
        }

        public void SetContent(string title, string detail, Texture icon = null, IEnumerable<VisualElement> actions = null)
        {
            Title.text = title ?? string.Empty;
            Detail.text = detail ?? string.Empty;
            Icon.image = icon;

            Icon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            Title.style.display = string.IsNullOrWhiteSpace(title) ? DisplayStyle.None : DisplayStyle.Flex;
            Detail.style.display = string.IsNullOrWhiteSpace(detail) ? DisplayStyle.None : DisplayStyle.Flex;

            Actions.Clear();
            if (actions != null)
            {
                foreach (VisualElement action in actions)
                {
                    if (action != null) Actions.Add(action);
                }
            }
            Actions.style.display = Actions.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
