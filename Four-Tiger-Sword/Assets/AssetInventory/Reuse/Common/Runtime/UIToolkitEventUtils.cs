using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public static class UIToolkitEventUtils
    {
        /// <summary>Consumes a handled event while preventing a second control action or focus change.</summary>
        public static void ConsumeEvent(EventBase evt, bool immediate = false)
        {
            if (evt == null)
                return;

#if UNITY_2023_2_OR_NEWER
            (evt.target as VisualElement)?.panel?.focusController?.IgnoreEvent(evt);
#else
            evt.PreventDefault();
#endif
            if (immediate)
                evt.StopImmediatePropagation();
            else
                evt.StopPropagation();
        }
    }
}
