using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Tool-agnostic semantic choice card for a small mutually exclusive option set.
    /// Group ownership stays with the caller so the selected value remains the source of truth.
    /// </summary>
    public sealed class CommonSingleChoiceCard : VisualElement
    {
        readonly Label _disabledReason;
        readonly Label _selectionMark;
        readonly string _availableTooltip;

        public event Action<CommonSingleChoiceCard> Chosen;

        public CommonSingleChoiceCard(
            string title,
            string description,
            string badge,
            string tooltip)
        {
            focusable = true;
            tabIndex = 0;
            AddToClassList("common-single-choice-card");
            _availableTooltip = tooltip ?? string.Empty;
            this.tooltip = _availableTooltip;

            VisualElement header = new VisualElement();
            header.AddToClassList("common-single-choice-card__header");
            _selectionMark = new Label("✓");
            _selectionMark.AddToClassList("common-single-choice-card__selection-mark");
            Label titleLabel = new Label(title ?? string.Empty);
            titleLabel.AddToClassList("common-single-choice-card__title");
            Label badgeLabel = new Label(badge ?? string.Empty);
            badgeLabel.AddToClassList("common-single-choice-card__badge");
            badgeLabel.style.display = string.IsNullOrWhiteSpace(badge)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            header.Add(_selectionMark);
            header.Add(titleLabel);
            header.Add(badgeLabel);
            Add(header);

            Label descriptionLabel = new Label(description ?? string.Empty);
            descriptionLabel.AddToClassList("common-single-choice-card__description");
            Add(descriptionLabel);

            _disabledReason = new Label();
            _disabledReason.AddToClassList("common-single-choice-card__disabled-reason");
            _disabledReason.style.display = DisplayStyle.None;
            Add(_disabledReason);

            RegisterCallback<ClickEvent>(_ => Choose());
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space)
                    return;
                Choose();
                evt.StopPropagation();
            });
        }

        public bool IsSelected { get; private set; }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            EnableInClassList("common-single-choice-card--selected", selected);
            _selectionMark.style.visibility = selected
                ? Visibility.Visible
                : Visibility.Hidden;
        }

        public void SetAvailable(bool available, string disabledReason = null)
        {
            SetEnabled(available);
            string reason = available ? string.Empty : disabledReason ?? string.Empty;
            _disabledReason.text = reason;
            _disabledReason.style.display = string.IsNullOrWhiteSpace(reason)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            EnableInClassList("common-single-choice-card--unavailable", !available);
            if (!available && !string.IsNullOrWhiteSpace(reason))
                tooltip = reason;
            else if (available)
                tooltip = _availableTooltip;
        }

        internal void Choose()
        {
            if (!enabledSelf || IsSelected)
                return;
            Chosen?.Invoke(this);
        }
    }
}
