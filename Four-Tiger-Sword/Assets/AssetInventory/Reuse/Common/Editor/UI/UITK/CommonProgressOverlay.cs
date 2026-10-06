using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonProgressOverlay : VisualElement
    {
        public sealed class ProgressOverlayClasses
        {
            public string RootClass;
            public string PanelClass;
            public string TitleClass;
            public string ProgressClass;
            public string DetailsClass;
        }

        public VisualElement Panel { get; }
        public Label Title { get; }
        public ProgressBar Progress { get; }
        public Label Details { get; }

        public CommonProgressOverlay(ProgressOverlayClasses classes = null)
        {
            ProgressOverlayClasses safeClasses = classes ?? new ProgressOverlayClasses();
            CommonUITK.AddClasses(this, safeClasses.RootClass);
            style.display = DisplayStyle.None;

            Panel = CommonUITK.CreateContainer(safeClasses.PanelClass);
            Add(Panel);

            Title = CommonUITK.CreateLabel(string.Empty, safeClasses.TitleClass);
            Panel.Add(Title);

            Progress = CommonUITK.CreateProgressBar(string.Empty, 0f, safeClasses.ProgressClass);
            Panel.Add(Progress);

            Details = CommonUITK.CreateLabel(string.Empty, safeClasses.DetailsClass);
            Panel.Add(Details);
        }

        public void SetState(string title, string progressTitle, float progress, string details)
        {
            Title.text = title ?? string.Empty;
            Progress.title = progressTitle ?? string.Empty;
            Progress.value = Mathf.Clamp01(progress);
            Details.text = details ?? string.Empty;
            style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
        }
    }
}
