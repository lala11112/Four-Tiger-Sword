using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ImpossibleRobert.Common
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class SearchablePopup
    {
        public struct PopupItem
        {
            public string Text;
            public Color BackgroundColor;
            public bool TintBackground;

            public PopupItem(string text)
            {
                Text = text;
                BackgroundColor = default;
                TintBackground = false;
            }

            public PopupItem(string text, Color backgroundColor, bool tintBackground = true)
            {
                Text = text;
                BackgroundColor = backgroundColor;
                TintBackground = tintBackground;
            }
        }

        private sealed class PopupState
        {
            public int LastKnownValue;
            public bool HasChanged;
            public bool ValueChangedThisFrame;
            public Rect CachedRect;
        }

        private static readonly Dictionary<int, PopupState> _popupStates = new Dictionary<int, PopupState>();
        private static readonly int _popupControlHint = "SearchablePopup".GetHashCode();

        public static int PopupField(int selectedIndex, string[] items, bool showBracketedValues = false, bool treatSlashLiterally = false, params GUILayoutOption[] options)
        {
            int controlId = GUIUtility.GetControlID(_popupControlHint, FocusType.Keyboard);
            return PopupFieldInternal(selectedIndex, items, null, false, showBracketedValues, treatSlashLiterally, controlId, options);
        }

        public static int PopupField(int selectedIndex, PopupItem[] items, bool tintSelectedField = false, bool showBracketedValues = false, bool treatSlashLiterally = false, params GUILayoutOption[] options)
        {
            int controlId = GUIUtility.GetControlID(_popupControlHint, FocusType.Keyboard);
            return PopupFieldInternal(selectedIndex, null, items, tintSelectedField, showBracketedValues, treatSlashLiterally, controlId, options);
        }

        public static int PopupField(int selectedIndex, string[] items, string stateKey, bool showBracketedValues = false, bool treatSlashLiterally = false, params GUILayoutOption[] options)
        {
            GUIUtility.GetControlID(_popupControlHint, FocusType.Keyboard);
            int stableKey = stateKey?.GetHashCode() ?? 0;
            return PopupFieldInternal(selectedIndex, items, null, false, showBracketedValues, treatSlashLiterally, stableKey, options);
        }

        public static int PopupField(int selectedIndex, PopupItem[] items, string stateKey, bool tintSelectedField = false, bool showBracketedValues = false, bool treatSlashLiterally = false, params GUILayoutOption[] options)
        {
            GUIUtility.GetControlID(_popupControlHint, FocusType.Keyboard);
            int stableKey = stateKey?.GetHashCode() ?? 0;
            return PopupFieldInternal(selectedIndex, null, items, tintSelectedField, showBracketedValues, treatSlashLiterally, stableKey, options);
        }

        private static int PopupFieldInternal(
            int selectedIndex,
            string[] items,
            PopupItem[] popupItems,
            bool tintSelectedField,
            bool showBracketedValues,
            bool treatSlashLiterally,
            int stateKey,
            GUILayoutOption[] options)
        {
            popupItems = popupItems ?? CreatePopupItems(items);
            items = items ?? Array.Empty<string>();
            if (items.Length == 0 && popupItems.Length > 0)
            {
                items = new string[popupItems.Length];
                for (int i = 0; i < popupItems.Length; i++)
                {
                    items[i] = popupItems[i].Text ?? string.Empty;
                }
            }

            int originalValue = selectedIndex;
            string displayText = GetSelectedDisplayText(selectedIndex, items, treatSlashLiterally);
            Rect popupRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, options);
            PopupState state = GetOrCreatePopupState(stateKey);

            if (Event.current.type == EventType.Layout)
            {
                state.CachedRect = popupRect;
                state.ValueChangedThisFrame = false;

                if (state.HasChanged)
                {
                    selectedIndex = state.LastKnownValue;
                    state.HasChanged = false;
                    state.ValueChangedThisFrame = true;
                    displayText = GetSelectedDisplayText(selectedIndex, items, treatSlashLiterally);
                }
                else
                {
                    state.LastKnownValue = selectedIndex;
                }
            }
            else if (Event.current.type == EventType.Repaint)
            {
                selectedIndex = state.LastKnownValue;
                displayText = GetSelectedDisplayText(selectedIndex, items, treatSlashLiterally);
            }

            bool tintField = ShouldTintSelectedField(popupItems, selectedIndex, tintSelectedField, out PopupItem selectedItem);
            Color oldBackgroundColor = GUI.backgroundColor;
            Color oldContentColor = GUI.contentColor;
            if (tintField)
            {
                GUI.backgroundColor = selectedItem.BackgroundColor;
                GUI.contentColor = CommonUIStyles.GetHSPColor(selectedItem.BackgroundColor);
            }

            if (GUI.Button(popupRect, new GUIContent(displayText), EditorStyles.popup))
            {
                float width = Mathf.Max(popupRect.width, 200f);
                int capturedStateKey = stateKey;
                int capturedSelectedIndex = selectedIndex;
                SearchablePopupWindow.ShowAsDropDown(
                    CommonUITK.ToScreenDropdownAnchor(popupRect),
                    popupItems,
                    selectedIndex,
                    index =>
                    {
                        if (capturedSelectedIndex == index) return;

                        PopupState callbackState = GetOrCreatePopupState(capturedStateKey);
                        callbackState.LastKnownValue = index;
                        callbackState.HasChanged = true;
                    },
                    width,
                    400f,
                    showBracketedValues,
                    treatSlashLiterally);
            }

            if (tintField)
            {
                GUI.backgroundColor = oldBackgroundColor;
                GUI.contentColor = oldContentColor;
            }

            if (state.ValueChangedThisFrame || selectedIndex != originalValue)
            {
                GUI.changed = true;
            }

            return selectedIndex;
        }

        private static PopupState GetOrCreatePopupState(int stateKey)
        {
            if (!_popupStates.TryGetValue(stateKey, out PopupState state))
            {
                state = new PopupState();
                _popupStates[stateKey] = state;
            }
            return state;
        }

        private static PopupItem[] CreatePopupItems(string[] items)
        {
            string[] source = items ?? Array.Empty<string>();
            PopupItem[] result = new PopupItem[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = new PopupItem(source[i]);
            }

            return result;
        }

        internal static string GetSelectedDisplayText(int selectedIndex, PopupItem[] items, bool treatSlashLiterally)
        {
            string displayText = selectedIndex >= 0 && selectedIndex < items.Length ? items[selectedIndex].Text ?? string.Empty : string.Empty;
            return GetSelectedDisplayText(displayText, treatSlashLiterally);
        }

        private static string GetSelectedDisplayText(int selectedIndex, string[] items, bool treatSlashLiterally)
        {
            string displayText = selectedIndex >= 0 && selectedIndex < items.Length ? items[selectedIndex] : string.Empty;
            return GetSelectedDisplayText(displayText, treatSlashLiterally);
        }

        private static string GetSelectedDisplayText(string displayText, bool treatSlashLiterally)
        {
            if (treatSlashLiterally) return displayText;

            int lastSlashIndex = displayText.LastIndexOf('/');
            if (lastSlashIndex >= 0 && lastSlashIndex < displayText.Length - 1)
            {
                displayText = displayText.Substring(lastSlashIndex + 1);
            }

            return displayText;
        }

        private static bool ShouldTintSelectedField(PopupItem[] items, int selectedIndex, bool tintSelectedField, out PopupItem popupItem)
        {
            if (items != null && tintSelectedField && selectedIndex >= 0 && selectedIndex < items.Length)
            {
                popupItem = items[selectedIndex];
                if (popupItem.TintBackground)
                {
                    return true;
                }
            }

            popupItem = default;
            return false;
        }
    }
}
