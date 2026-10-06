using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace ImpossibleRobert.Common
{
    public sealed class CommonInlineLinkLabel : Label
    {
        private readonly Action<string> _openLink;
        private readonly List<string> _urls = new List<string>();
        private readonly string _hoverClass;

        public CommonInlineLinkLabel(
            TextWithLinks content,
            Action<string> openLink,
            Color linkColor,
            string hoverClass = null)
        {
            _openLink = openLink;
            _hoverClass = hoverClass;
            enableRichText = true;
            text = BuildRichText(content, linkColor);

            RegisterCallback<PointerUpLinkTagEvent>(OnLinkPointerUp);
            RegisterCallback<PointerOverLinkTagEvent>(OnLinkPointerOver);
            RegisterCallback<PointerOutLinkTagEvent>(OnLinkPointerOut);
        }

        private string BuildRichText(TextWithLinks content, Color linkColor)
        {
            string plainText = content.Text ?? string.Empty;
            List<TextLink> links = content.Links;
            if (links == null || links.Count == 0) return plainText;

            string color = ColorUtility.ToHtmlStringRGB(linkColor);
            StringBuilder builder = new StringBuilder(plainText.Length + links.Count * 64);
            int cursor = 0;
            for (int i = 0; i < links.Count; i++)
            {
                TextLink link = links[i];
                if (link.StartIndex < cursor || link.Length <= 0 || link.StartIndex + link.Length > plainText.Length) continue;

                AppendLiteral(builder, plainText, cursor, link.StartIndex - cursor);
                int linkId = _urls.Count;
                _urls.Add(link.Url);
                builder.Append("<link=\"");
                builder.Append(linkId);
                builder.Append("\"><color=#");
                builder.Append(color);
                builder.Append("><u>");
                AppendLiteral(builder, plainText, link.StartIndex, link.Length);
                builder.Append("</u></color></link>");
                cursor = link.StartIndex + link.Length;
            }
            AppendLiteral(builder, plainText, cursor, plainText.Length - cursor);
            return builder.ToString();
        }

        private static void AppendLiteral(StringBuilder builder, string text, int startIndex, int length)
        {
            if (length <= 0) return;

            builder.Append("<noparse>");
            builder.Append(text, startIndex, length);
            builder.Append("</noparse>");
        }

        private void OnLinkPointerUp(PointerUpLinkTagEvent evt)
        {
            if (evt.button != 0 || _openLink == null) return;
            if (!TryGetUrl(evt.linkID, out string url)) return;

            _openLink(url);
            evt.StopPropagation();
        }

        private void OnLinkPointerOver(PointerOverLinkTagEvent evt)
        {
            if (!string.IsNullOrWhiteSpace(_hoverClass)) AddToClassList(_hoverClass);
            if (TryGetUrl(evt.linkID, out string url)) tooltip = url;
        }

        private void OnLinkPointerOut(PointerOutLinkTagEvent evt)
        {
            if (!string.IsNullOrWhiteSpace(_hoverClass)) RemoveFromClassList(_hoverClass);
            tooltip = string.Empty;
        }

        private bool TryGetUrl(string linkId, out string url)
        {
            url = null;
            if (!int.TryParse(linkId, out int index) || index < 0 || index >= _urls.Count) return false;

            url = _urls[index];
            return !string.IsNullOrWhiteSpace(url);
        }
    }
}
