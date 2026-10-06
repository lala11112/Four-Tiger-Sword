using System;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonOrderedSection : VisualElement
    {
        public sealed class OrderedSectionClasses
        {
            public string RootClass;
            public string CustomizationClass;
            public string ControlsClass;
            public string MoveButtonClass;
            public string BodyClass;
        }

        public VisualElement Controls { get; }
        public VisualElement Body { get; }

        public CommonOrderedSection(
            VisualElement content,
            bool customizationMode,
            bool canMoveUp,
            bool canMoveDown,
            Action moveUp,
            Action moveDown,
            OrderedSectionClasses classes = null)
        {
            OrderedSectionClasses safeClasses = classes ?? new OrderedSectionClasses();
            CommonUITK.AddClasses(this, safeClasses.RootClass);

            Controls = CommonUITK.CreateContainer(safeClasses.ControlsClass);
            Controls.style.display = customizationMode ? DisplayStyle.Flex : DisplayStyle.None;
            if (customizationMode)
            {
                CommonUITK.AddClasses(this, safeClasses.CustomizationClass);

                Button up = CommonUITK.CreateButton("^", moveUp, safeClasses.MoveButtonClass);
                up.tooltip = "Move section up";
                up.SetEnabled(canMoveUp);
                Controls.Add(up);

                Button down = CommonUITK.CreateButton("v", moveDown, safeClasses.MoveButtonClass);
                down.tooltip = "Move section down";
                down.SetEnabled(canMoveDown);
                Controls.Add(down);
            }
            Add(Controls);

            Body = CommonUITK.CreateContainer(safeClasses.BodyClass);
            if (content != null) Body.Add(content);
            Add(Body);
        }
    }
}
