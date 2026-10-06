using System;
using ImpossibleRobert.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioTool
{
    internal sealed class AudioWaveformElement : VisualElement
    {
        private const float HandleHitWidth = 10f;
        private const float DragThreshold = 3f;
        private const float TimelineLabelWidth = 62f;
        private const float TimelineFirstRowY = 2f;
        private const float TimelineSecondRowY = 19f;
        private const float TimelineThirdRowY = 36f;
        private const float TimelineLabelGap = 6f;

        private enum DragMode
        {
            None,
            Pending,
            SelectionStart,
            SelectionEnd,
            SelectionBody,
            NewSelection,
            Pan
        }

        private readonly CommonUITKMeshBuilder _meshBuilder = new CommonUITKMeshBuilder(16000);
        private readonly VisualElement _canvas;
        private readonly VisualElement _timeline;
        private readonly VisualElement _durationConnector;
        private readonly Label _viewportStartLabel;
        private readonly Label _viewportEndLabel;
        private readonly Label _selectionStartLabel;
        private readonly Label _selectionEndLabel;
        private readonly Label _selectionDurationLabel;
        private readonly Label _playheadLabel;
        private AudioEditSession _session;
        private AudioWaveformPeakCache _peaks;
        private float _clipDuration = 1f;
        private float _pointerDownX;
        private float _pointerDownNormalized;
        private float _selectionStartAtPointerDown;
        private float _selectionEndAtPointerDown;
        private float _viewportStartAtPointerDown;
        private int _pointerId = -1;
        private DragMode _dragMode;

        internal event Action EditGestureStarted;
        internal event Action EditGestureFinished;
        internal event Action<float> SeekRequested;
        internal event Action SelectionChanged;
        internal event Action ViewportChanged;

        internal AudioWaveformElement()
        {
            name = "audio-waveform";
            AddToClassList("at-waveform-layout");

            _canvas = new VisualElement
            {
                name = "audio-waveform-canvas",
                focusable = true,
                tabIndex = 0
            };
            _canvas.AddToClassList("at-waveform");
            _canvas.generateVisualContent += Draw;
            _canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _canvas.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            _canvas.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            _canvas.RegisterCallback<GeometryChangedEvent>(_ => UpdateTimeline());
            Add(_canvas);

            _timeline = new VisualElement { name = "audio-waveform-timeline" };
            _timeline.AddToClassList("at-waveform-timeline");
            Add(_timeline);

            _viewportStartLabel = CreateTimelineLabel("audio-viewport-start", "at-waveform-boundary");
            _viewportEndLabel = CreateTimelineLabel("audio-viewport-end", "at-waveform-boundary");
            _selectionStartLabel = CreateTimelineLabel("audio-selection-start-label", "at-waveform-selection-time");
            _selectionEndLabel = CreateTimelineLabel("audio-selection-end-label", "at-waveform-selection-time");
            _selectionDurationLabel = CreateTimelineLabel("audio-selection-duration-label", "at-waveform-duration");
            _playheadLabel = CreateTimelineLabel("audio-playhead-time", "at-waveform-playhead-time");
            _durationConnector = new VisualElement { name = "audio-selection-duration-connector" };
            _durationConnector.AddToClassList("at-waveform-duration-connector");

            _timeline.Add(_durationConnector);
            _timeline.Add(_viewportStartLabel);
            _timeline.Add(_viewportEndLabel);
            _timeline.Add(_selectionStartLabel);
            _timeline.Add(_selectionEndLabel);
            _timeline.Add(_selectionDurationLabel);
            _timeline.Add(_playheadLabel);
        }

        internal void Bind(AudioEditSession session, AudioWaveformPeakCache peaks, float clipDuration)
        {
            _session = session;
            _peaks = peaks;
            _clipDuration = Mathf.Max(0.0001f, clipDuration);
            Refresh();
        }

        internal void Refresh()
        {
            _canvas.MarkDirtyRepaint();
            UpdateTimeline();
        }

        internal float PixelDeltaToNormalized(float pixels)
        {
            float width = Mathf.Max(1f, _canvas.contentRect.width);
            return pixels / width * GetViewportWidth();
        }

        private static Label CreateTimelineLabel(string name, string className)
        {
            Label label = new Label
            {
                name = name,
                pickingMode = PickingMode.Ignore
            };
            label.AddToClassList("at-waveform-time");
            label.AddToClassList(className);
            return label;
        }

        private void UpdateTimeline()
        {
            float width = _canvas.contentRect.width;
            if (_session == null || width < 2f)
            {
                SetDisplayed(_selectionStartLabel, false);
                SetDisplayed(_selectionEndLabel, false);
                SetDisplayed(_selectionDurationLabel, false);
                SetDisplayed(_playheadLabel, false);
                SetDisplayed(_durationConnector, false);
                return;
            }

            float viewportEnd = _session.ViewportStart + GetViewportWidth();
            _viewportStartLabel.text = FormatTime(_session.ViewportStart * _clipDuration);
            _viewportEndLabel.text = FormatTime(viewportEnd * _clipDuration);
            SetLabelPosition(_viewportStartLabel, 0f, TimelineFirstRowY);
            SetLabelPosition(_viewportEndLabel, width - TimelineLabelWidth, TimelineFirstRowY);
            SetDisplayed(_viewportStartLabel, true);
            SetDisplayed(_viewportEndLabel, true);

            bool startVisible = _session.HasSelection && IsVisible(_session.SelectionStart);
            bool endVisible = _session.HasSelection && IsVisible(_session.SelectionEnd);
            float startLeft = 0f;
            float endLeft = 0f;
            if (startVisible)
            {
                startLeft = ClampLabelLeft(NormalizedToX(_session.SelectionStart) - TimelineLabelWidth * 0.5f, width);
                _selectionStartLabel.text = FormatTime(_session.SelectionStart * _clipDuration);
            }
            if (endVisible)
            {
                endLeft = ClampLabelLeft(NormalizedToX(_session.SelectionEnd) - TimelineLabelWidth * 0.5f, width);
                _selectionEndLabel.text = FormatTime(_session.SelectionEnd * _clipDuration);
            }

            if (startVisible && endVisible && LabelsOverlap(startLeft, endLeft))
            {
                float center = (NormalizedToX(_session.SelectionStart) + NormalizedToX(_session.SelectionEnd)) * 0.5f;
                float groupWidth = TimelineLabelWidth * 2f + TimelineLabelGap;
                float groupLeft = Mathf.Clamp(center - groupWidth * 0.5f, 0f, Mathf.Max(0f, width - groupWidth));
                startLeft = groupLeft;
                endLeft = groupLeft + TimelineLabelWidth + TimelineLabelGap;
            }

            if (startVisible) SetLabelPosition(_selectionStartLabel, startLeft, TimelineFirstRowY);
            if (endVisible) SetLabelPosition(_selectionEndLabel, endLeft, TimelineFirstRowY);
            SetDisplayed(_selectionStartLabel, startVisible);
            SetDisplayed(_selectionEndLabel, endVisible);

            bool durationVisible = startVisible && endVisible;
            float durationY = TimelineFirstRowY;
            if (durationVisible)
            {
                float durationCenter = (NormalizedToX(_session.SelectionStart) + NormalizedToX(_session.SelectionEnd)) * 0.5f;
                float durationLeft = ClampLabelLeft(durationCenter - TimelineLabelWidth * 0.5f, width);
                if (LabelsOverlap(durationLeft, startLeft) || LabelsOverlap(durationLeft, endLeft))
                {
                    durationY = TimelineSecondRowY;
                }

                _selectionDurationLabel.text = FormatTime((_session.SelectionEnd - _session.SelectionStart) * _clipDuration);
                SetLabelPosition(_selectionDurationLabel, durationLeft, durationY);
                bool needsConnector = durationY > TimelineFirstRowY;
                SetDisplayed(_durationConnector, needsConnector);
                if (needsConnector)
                {
                    _durationConnector.style.left = Mathf.Clamp(durationCenter, 0f, width - 1f);
                }
            }
            else
            {
                SetDisplayed(_durationConnector, false);
            }
            SetDisplayed(_selectionDurationLabel, durationVisible);

            bool playheadVisible = IsVisible(_session.Playhead);
            if (playheadVisible)
            {
                float playheadLeft = ClampLabelLeft(NormalizedToX(_session.Playhead) - TimelineLabelWidth * 0.5f, width);
                float playheadY = durationVisible && durationY > TimelineFirstRowY ? TimelineThirdRowY : TimelineSecondRowY;
                _playheadLabel.text = FormatTime(_session.Playhead * _clipDuration);
                SetLabelPosition(_playheadLabel, playheadLeft, playheadY);
            }
            SetDisplayed(_playheadLabel, playheadVisible);

            if (startVisible && LabelsOverlap(startLeft, 0f)) SetDisplayed(_viewportStartLabel, false);
            if (endVisible && LabelsOverlap(endLeft, width - TimelineLabelWidth)) SetDisplayed(_viewportEndLabel, false);
        }

        private bool IsVisible(float normalized)
        {
            float x = NormalizedToX(normalized);
            return x >= 0f && x <= _canvas.contentRect.width;
        }

        private static bool LabelsOverlap(float left, float otherLeft)
        {
            return left < otherLeft + TimelineLabelWidth + TimelineLabelGap && otherLeft < left + TimelineLabelWidth + TimelineLabelGap;
        }

        private static float ClampLabelLeft(float left, float width)
        {
            return Mathf.Clamp(left, 0f, Mathf.Max(0f, width - TimelineLabelWidth));
        }

        private static void SetLabelPosition(VisualElement label, float left, float top)
        {
            label.style.left = left;
            label.style.top = top;
        }

        private static void SetDisplayed(VisualElement element, bool displayed)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int minutes = Mathf.FloorToInt(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            return minutes + ":" + remainder.ToString("00.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = _canvas.contentRect;
            if (_session == null || _peaks == null || rect.width < 2f || rect.height < 2f) return;

            _meshBuilder.Clear();
            float viewportStart = _session.ViewportStart;
            float viewportWidth = GetViewportWidth();
            float viewportEnd = viewportStart + viewportWidth;

            if (_session.HasSelection)
            {
                float selectionLeft = NormalizedToX(_session.SelectionStart);
                float selectionRight = NormalizedToX(_session.SelectionEnd);
                float clippedLeft = Mathf.Clamp(selectionLeft, 0f, rect.width);
                float clippedRight = Mathf.Clamp(selectionRight, 0f, rect.width);
                if (clippedRight > clippedLeft)
                {
                    _meshBuilder.AddRect(new Rect(clippedLeft, 0f, clippedRight - clippedLeft, rect.height), new Color(0.1f, 0.55f, 0.95f, 0.18f));
                }
            }

            int channels = Mathf.Max(1, _peaks.Channels);
            float channelHeight = rect.height / channels;
            Color centerColor = new Color(0.45f, 0.52f, 0.61f, 0.28f);
            Color waveformColor = new Color(0.24f, 0.68f, 1f, 1f);
            Color clippingColor = new Color(1f, 0.35f, 0.28f, 1f);
            int columns = Mathf.Clamp(Mathf.CeilToInt(rect.width), 1, 4096);

            for (int channel = 0; channel < channels; channel++)
            {
                float top = channel * channelHeight;
                float center = top + channelHeight * 0.5f;
                float amplitude = Mathf.Max(1f, channelHeight * 0.47f);
                _meshBuilder.AddLine(new Vector2(0f, center), new Vector2(rect.width, center), 1f, centerColor);

                for (int column = 0; column < columns; column++)
                {
                    float t0 = (float)column / columns;
                    float t1 = (float)(column + 1) / columns;
                    float rangeStart = Mathf.Lerp(viewportStart, viewportEnd, t0);
                    float rangeEnd = Mathf.Lerp(viewportStart, viewportEnd, t1);
                    _peaks.GetRange(channel, rangeStart, rangeEnd, out float minimum, out float maximum);
                    float x = t0 * rect.width;
                    Color color = maximum >= 0.999f || minimum <= -0.999f ? clippingColor : waveformColor;
                    _meshBuilder.AddLine(
                        new Vector2(x, center - maximum * amplitude),
                        new Vector2(x, center - minimum * amplitude),
                        1.25f,
                        color);
                }
            }

            DrawFadeEnvelope(rect);

            if (_session.HasSelection)
            {
                float selectionLeft = NormalizedToX(_session.SelectionStart);
                float selectionRight = NormalizedToX(_session.SelectionEnd);
                Color handleColor = new Color(0.32f, 0.76f, 1f, 1f);
                if (selectionLeft >= 0f && selectionLeft <= rect.width)
                {
                    _meshBuilder.AddRect(new Rect(selectionLeft - 1.5f, 0f, 3f, rect.height), handleColor);
                    _meshBuilder.AddRect(new Rect(selectionLeft - 4f, 0f, 8f, 5f), handleColor);
                }
                if (selectionRight >= 0f && selectionRight <= rect.width)
                {
                    _meshBuilder.AddRect(new Rect(selectionRight - 1.5f, 0f, 3f, rect.height), handleColor);
                    _meshBuilder.AddRect(new Rect(selectionRight - 4f, 0f, 8f, 5f), handleColor);
                }
            }

            float playheadX = NormalizedToX(_session.Playhead);
            if (playheadX >= 0f && playheadX <= rect.width)
            {
                Color playheadColor = new Color(1f, 0.34f, 0.3f, 1f);
                _meshBuilder.AddRect(new Rect(playheadX - 1f, 0f, 2f, rect.height), playheadColor);
                _meshBuilder.AddTriangle(new Vector2(playheadX - 5f, 0f), new Vector2(playheadX + 5f, 0f), new Vector2(playheadX, 7f), playheadColor);
            }

            _meshBuilder.Flush(context);
        }

        private void DrawFadeEnvelope(Rect rect)
        {
            if (_session == null || (!_session.FadeIn && !_session.FadeOut)) return;

            float regionStart = _session.HasSelection ? _session.SelectionStart : 0f;
            float regionEnd = _session.HasSelection ? _session.SelectionEnd : 1f;
            Color fadeColor = new Color(1f, 0.76f, 0.25f, 0.95f);

            if (_session.FadeIn)
            {
                float fadeEnd = Mathf.Min(regionEnd, regionStart + _session.FadeInDuration / _clipDuration);
                float previousX = NormalizedToX(regionStart);
                float previousY = rect.height - 2f;
                for (int i = 1; i <= 24; i++)
                {
                    float t = i / 24f;
                    float x = NormalizedToX(Mathf.Lerp(regionStart, fadeEnd, t));
                    float y = Mathf.Lerp(rect.height - 2f, 2f, Mathf.Clamp01(_session.FadeInCurve.Evaluate(t)));
                    _meshBuilder.AddLine(new Vector2(previousX, previousY), new Vector2(x, y), 1.5f, fadeColor);
                    previousX = x;
                    previousY = y;
                }
            }

            if (_session.FadeOut)
            {
                float fadeStart = Mathf.Max(regionStart, regionEnd - _session.FadeOutDuration / _clipDuration);
                float previousX = NormalizedToX(fadeStart);
                float previousY = 2f;
                for (int i = 1; i <= 24; i++)
                {
                    float t = i / 24f;
                    float x = NormalizedToX(Mathf.Lerp(fadeStart, regionEnd, t));
                    float y = Mathf.Lerp(2f, rect.height - 2f, t);
                    _meshBuilder.AddLine(new Vector2(previousX, previousY), new Vector2(x, y), 1.5f, fadeColor);
                    previousX = x;
                    previousY = y;
                }
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (_session == null || evt.button != 0 && evt.button != 1) return;

            _canvas.Focus();
            _pointerId = evt.pointerId;
            _pointerDownX = evt.localPosition.x;
            _pointerDownNormalized = XToNormalized(_pointerDownX);
            _selectionStartAtPointerDown = _session.SelectionStart;
            _selectionEndAtPointerDown = _session.SelectionEnd;
            _viewportStartAtPointerDown = _session.ViewportStart;
            if (evt.button == 1)
            {
                _dragMode = DragMode.Pan;
                PointerCaptureHelper.CapturePointer(_canvas, evt.pointerId);
                evt.StopPropagation();
                return;
            }

            _dragMode = GetDragMode(_pointerDownX);
            PointerCaptureHelper.CapturePointer(_canvas, evt.pointerId);
            EditGestureStarted?.Invoke();
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_session == null || evt.pointerId != _pointerId || !PointerCaptureHelper.HasPointerCapture(_canvas, evt.pointerId)) return;

            float currentX = evt.localPosition.x;
            if (_dragMode == DragMode.Pan)
            {
                float pixelDelta = currentX - _pointerDownX;
                _session.ViewportStart = _viewportStartAtPointerDown - pixelDelta / Mathf.Max(1f, _canvas.contentRect.width) * GetViewportWidth();
                _session.ClampViewport();
                ViewportChanged?.Invoke();
                Refresh();
                evt.StopPropagation();
                return;
            }

            float current = XToNormalized(currentX);
            if (_dragMode == DragMode.Pending && Mathf.Abs(currentX - _pointerDownX) >= DragThreshold)
            {
                _dragMode = DragMode.NewSelection;
                _session.HasSelection = true;
            }

            switch (_dragMode)
            {
                case DragMode.SelectionStart:
                    _session.SelectionStart = Mathf.Min(current, _session.SelectionEnd - MinimumSelectionWidth());
                    break;
                case DragMode.SelectionEnd:
                    _session.SelectionEnd = Mathf.Max(current, _session.SelectionStart + MinimumSelectionWidth());
                    break;
                case DragMode.SelectionBody:
                    float width = _selectionEndAtPointerDown - _selectionStartAtPointerDown;
                    float delta = current - _pointerDownNormalized;
                    float start = Mathf.Clamp(_selectionStartAtPointerDown + delta, 0f, 1f - width);
                    _session.SelectionStart = start;
                    _session.SelectionEnd = start + width;
                    break;
                case DragMode.NewSelection:
                    _session.SelectionStart = Mathf.Min(_pointerDownNormalized, current);
                    _session.SelectionEnd = Mathf.Max(_pointerDownNormalized, current);
                    break;
                default:
                    return;
            }

            _session.NormalizeSelection(MinimumSelectionWidth());
            _session.Playhead = _session.SelectionStart;
            SelectionChanged?.Invoke();
            Refresh();
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointerId) return;

            if (_dragMode == DragMode.Pending || _dragMode == DragMode.SelectionBody && Mathf.Abs(evt.localPosition.x - _pointerDownX) < DragThreshold)
            {
                _session.Playhead = XToNormalized(evt.localPosition.x);
                if (_dragMode == DragMode.Pending && _session.HasSelection)
                {
                    _session.ClearSelection();
                    SelectionChanged?.Invoke();
                }
                SeekRequested?.Invoke(_session.Playhead);
            }

            ReleasePointerIfNeeded(evt.pointerId);
            FinishGesture();
            evt.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId == _pointerId) FinishGesture();
        }

        private void OnWheel(WheelEvent evt)
        {
            if (_session == null) return;

            if (evt.actionKey)
            {
                float anchor = XToNormalized(evt.localMousePosition.x);
                float oldWidth = GetViewportWidth();
                float multiplier = evt.delta.y > 0f ? 0.84f : 1.19f;
                _session.Zoom *= multiplier;
                float newWidth = GetViewportWidth();
                float anchorRatio = Mathf.Clamp01(evt.localMousePosition.x / Mathf.Max(1f, _canvas.contentRect.width));
                _session.ViewportStart = anchor - anchorRatio * newWidth;
                if (Mathf.Approximately(oldWidth, newWidth)) return;
            }
            else
            {
                _session.ViewportStart += evt.delta.y * GetViewportWidth() * 0.035f;
            }

            _session.ClampViewport();
            ViewportChanged?.Invoke();
            Refresh();
            evt.StopPropagation();
        }

        private DragMode GetDragMode(float x)
        {
            if (!_session.HasSelection) return DragMode.Pending;
            float startX = NormalizedToX(_session.SelectionStart);
            float endX = NormalizedToX(_session.SelectionEnd);
            if (Mathf.Abs(x - startX) <= HandleHitWidth) return DragMode.SelectionStart;
            if (Mathf.Abs(x - endX) <= HandleHitWidth) return DragMode.SelectionEnd;
            if (x > startX && x < endX) return DragMode.SelectionBody;
            return DragMode.Pending;
        }

        private void FinishGesture()
        {
            if (_dragMode != DragMode.None && _dragMode != DragMode.Pan) EditGestureFinished?.Invoke();
            _dragMode = DragMode.None;
            _pointerId = -1;
        }

        private void ReleasePointerIfNeeded(int pointerId)
        {
            if (PointerCaptureHelper.HasPointerCapture(_canvas, pointerId)) PointerCaptureHelper.ReleasePointer(_canvas, pointerId);
        }

        private float XToNormalized(float x)
        {
            float ratio = Mathf.Clamp01(x / Mathf.Max(1f, _canvas.contentRect.width));
            return Mathf.Clamp01(_session.ViewportStart + ratio * GetViewportWidth());
        }

        private float NormalizedToX(float normalized)
        {
            return (normalized - _session.ViewportStart) / GetViewportWidth() * _canvas.contentRect.width;
        }

        private float GetViewportWidth()
        {
            return _session == null ? 1f : _session.ViewportDurationNormalized;
        }

        private float MinimumSelectionWidth()
        {
            return Mathf.Max(0.000001f, PixelDeltaToNormalized(1f));
        }
    }
}
