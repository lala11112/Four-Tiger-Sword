using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonScrollViewState
    {
        private readonly Dictionary<string, Vector2> _offsets = new Dictionary<string, Vector2>();
        private readonly Dictionary<ScrollView, Vector2> _pendingRestores = new Dictionary<ScrollView, Vector2>();

        public void Capture(string key, ScrollView scrollView)
        {
            if (string.IsNullOrEmpty(key) || scrollView == null) return;

            if (_pendingRestores.TryGetValue(scrollView, out Vector2 pendingOffset))
            {
                _offsets[key] = pendingOffset;
                _pendingRestores.Remove(scrollView);
                return;
            }

            _offsets[key] = scrollView.scrollOffset;
        }

        public void Restore(string key, ScrollView scrollView)
        {
            if (string.IsNullOrEmpty(key) || scrollView == null || !_offsets.TryGetValue(key, out Vector2 offset)) return;

            _pendingRestores[scrollView] = offset;
            scrollView.scrollOffset = offset;
            scrollView.schedule.Execute(() =>
            {
                if (scrollView.panel != null) scrollView.scrollOffset = offset;
                _pendingRestores.Remove(scrollView);
            }).ExecuteLater(0);
        }

        public Vector2 GetOffset(string key)
        {
            return !string.IsNullOrEmpty(key) && _offsets.TryGetValue(key, out Vector2 offset)
                ? offset
                : Vector2.zero;
        }
    }
}
