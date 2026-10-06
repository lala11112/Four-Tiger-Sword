using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>Shared preview card presentation and input, without texture ownership or catalog state.</summary>
    public sealed class CommonPreviewGalleryCard : VisualElement
    {
        readonly Action _select;
        readonly Action _activate;

        public Image Preview { get; }
        public Label Title { get; }
        public Label Description { get; }

        public CommonPreviewGalleryCard(string title, string description, Action select, Action activate)
        {
            _select = select;
            _activate = activate;
            focusable = true;
            AddToClassList("common-inspector-section");
            AddToClassList("common-preview-gallery-card");
            Preview = new Image { name = "common-preview-gallery-card-preview", scaleMode = ScaleMode.ScaleToFit };
            Preview.AddToClassList("common-preview-gallery-card__preview");
            Add(Preview);
            Title = CommonUITK.CreateLabel(title, "common-preview-gallery-card__title");
            Add(Title);
            Description = CommonInspectorElements.CreateMutedText(description);
            Description.AddToClassList("common-preview-gallery-card__description");
            Add(Description);

            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 && evt.button != 1)
                    return;
                Focus();
                _select?.Invoke();
                if (evt.button == 0 && evt.clickCount >= 2)
                {
                    _activate?.Invoke();
                    evt.StopPropagation();
                }
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Space && evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;
                _select?.Invoke();
                if (evt.keyCode != KeyCode.Space)
                    _activate?.Invoke();
                CommonUITK.ConsumeEvent(evt);
            });
            RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                _select?.Invoke();
                _activate?.Invoke();
                evt.StopPropagation();
            });
        }

        public void SetSelected(bool selected)
        {
            EnableInClassList("common-preview-gallery-card--selected", selected);
        }

        public void SetPresentation(float width, float previewHeight, bool showDescription)
        {
            style.width = width;
            Preview.style.height = previewHeight;
            Description.style.display = showDescription && !string.IsNullOrWhiteSpace(Description.text)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }
}
