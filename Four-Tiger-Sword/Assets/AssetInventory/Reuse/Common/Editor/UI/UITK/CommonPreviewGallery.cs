using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Retained gallery chrome shared by preview browsers. Consumers own their catalog,
    /// filtering, selection, and preview resources; this view does not create or render items.
    /// </summary>
    public sealed class CommonPreviewGallery : VisualElement
    {
        public Toolbar Toolbar { get; }
        public ToolbarSearchField Search { get; }
        public ToolbarMenu Filter { get; }
        public Slider Size { get; }
        public ToolbarToggle Descriptions { get; }
        public VisualElement Options { get; }
        public ScrollView Grid { get; }
        public VisualElement EmptyState { get; }

        public CommonPreviewGallery(string namePrefix, string emptyMessage, float minCardWidth = 200f, float maxCardWidth = 400f)
        {
            name = namePrefix;
            AddToClassList("common-preview-gallery");
            StyleSheet sheet = CommonUITK.LoadStyleSheetFromAnchor(
                "CommonPreviewGallery", "Editor/UI/UITK/CommonPreviewGallery.cs", "Editor/UI/UITK/CommonPreviewGallery.uss");
            if (sheet != null)
                styleSheets.Add(sheet);

            Toolbar = new Toolbar();
            Toolbar.AddToClassList("common-preview-gallery__toolbar");
            VisualElement filters = CommonUITK.CreateContainer(
                "common-preview-gallery__toolbar-group", "common-preview-gallery__toolbar-group--filters");
            Search = new ToolbarSearchField { name = namePrefix + "-search", tooltip = "Search previews" };
            Search.AddToClassList("common-preview-gallery__search");
            filters.Add(Search);
            Filter = new ToolbarMenu { name = namePrefix + "-filter", text = "All", tooltip = "Filter previews by category" };
            filters.Add(Filter);
            Toolbar.Add(filters);

            Options = CommonUITK.CreateContainer(
                "common-preview-gallery__toolbar-group", "common-preview-gallery__toolbar-group--options");
            Options.Add(CommonUITK.CreateLabel("Size", "common-preview-gallery__size-label"));
            Size = new Slider(minCardWidth, maxCardWidth) { name = namePrefix + "-size", tooltip = "Preview card size" };
            Size.AddToClassList("common-preview-gallery__size");
            Options.Add(Size);
            Descriptions = new ToolbarToggle
            {
                name = namePrefix + "-descriptions",
                text = "Descriptions",
                tooltip = "Show descriptions below preview titles"
            };
            Options.Add(Descriptions);
            Toolbar.Add(Options);
            Add(Toolbar);

            Grid = new ScrollView(ScrollViewMode.Vertical) { name = namePrefix + "-grid" };
            Grid.AddToClassList("common-preview-gallery__grid");
            Grid.contentContainer.AddToClassList("common-preview-gallery__cards");
            Add(Grid);
            EmptyState = CommonInspectorElements.CreateHelpBox(emptyMessage, HelpBoxMessageType.Info);
            EmptyState.style.display = DisplayStyle.None;
            Grid.Add(EmptyState);
        }

        public void ClearCards()
        {
            Grid.Clear();
            Grid.Add(EmptyState);
        }

        public void SetEmptyStateVisible(bool visible)
        {
            EmptyState.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
