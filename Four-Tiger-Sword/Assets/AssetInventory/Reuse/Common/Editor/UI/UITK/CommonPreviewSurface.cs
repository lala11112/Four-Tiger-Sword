using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Stable native UI Toolkit presentation surface for tool-owned preview textures.
    /// The consuming inspector remains responsible for rendering and releasing preview resources.
    /// </summary>
    public sealed class CommonPreviewSurface : VisualElement
    {
        readonly Label _emptyTitle;
        readonly Label _emptyDetail;

        public CommonPreviewSurface()
        {
            AddToClassList("common-preview");

            PreviewImage = new Image
            {
                name = "common-preview-image",
                scaleMode = ScaleMode.ScaleToFit
            };
            PreviewImage.AddToClassList("common-preview__image");
            PreviewImage.style.display = DisplayStyle.None;
            Add(PreviewImage);

            EmptyState = CommonUITK.CreateContainer("common-preview__empty");
            _emptyTitle = CommonUITK.CreateLabel("Preview unavailable", "common-preview__empty-title");
            _emptyDetail = CommonUITK.CreateLabel(
                "Configure the required fields to see a preview.",
                "common-preview__empty-detail");
            EmptyState.Add(_emptyTitle);
            EmptyState.Add(_emptyDetail);
            Add(EmptyState);

            Overlay = CommonUITK.CreateContainer("common-preview__overlay");
            Add(Overlay);

            StatusLabel = CommonUITK.CreateLabel(string.Empty, "common-preview__status");
            StatusLabel.style.display = DisplayStyle.None;
            Overlay.Add(StatusLabel);
        }

        public Image PreviewImage { get; }

        public VisualElement EmptyState { get; }

        public VisualElement Overlay { get; }

        public Label StatusLabel { get; }

        public void SetTexture(Texture texture, ScaleMode scaleMode = ScaleMode.ScaleToFit)
        {
            if (PreviewImage.image != texture)
                PreviewImage.image = texture;
            if (PreviewImage.scaleMode != scaleMode)
                PreviewImage.scaleMode = scaleMode;
            bool hasTexture = texture != null;
            DisplayStyle previewDisplay = hasTexture ? DisplayStyle.Flex : DisplayStyle.None;
            DisplayStyle emptyDisplay = hasTexture ? DisplayStyle.None : DisplayStyle.Flex;
            if (PreviewImage.style.display.value != previewDisplay)
                PreviewImage.style.display = previewDisplay;
            if (EmptyState.style.display.value != emptyDisplay)
                EmptyState.style.display = emptyDisplay;
        }

        public void SetEmptyState(string title, string detail)
        {
            PreviewImage.image = null;
            PreviewImage.style.display = DisplayStyle.None;
            _emptyTitle.text = string.IsNullOrWhiteSpace(title) ? "Preview unavailable" : title;
            _emptyDetail.text = detail ?? string.Empty;
            _emptyDetail.style.display = string.IsNullOrWhiteSpace(detail)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            EmptyState.style.display = DisplayStyle.Flex;
        }

        public void SetStatus(string text, CommonInspectorStatusType statusType)
        {
            bool visible = !string.IsNullOrWhiteSpace(text);
            string resolvedText = text ?? string.Empty;
            if (StatusLabel.text != resolvedText)
                StatusLabel.text = resolvedText;
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (StatusLabel.style.display.value != display)
                StatusLabel.style.display = display;
            StatusLabel.EnableInClassList("common-status--info", statusType == CommonInspectorStatusType.Info);
            StatusLabel.EnableInClassList("common-status--success", statusType == CommonInspectorStatusType.Success);
            StatusLabel.EnableInClassList("common-status--warning", statusType == CommonInspectorStatusType.Warning);
            StatusLabel.EnableInClassList("common-status--error", statusType == CommonInspectorStatusType.Error);
            StatusLabel.EnableInClassList("common-status--pending", statusType == CommonInspectorStatusType.Pending);
        }
    }
}
