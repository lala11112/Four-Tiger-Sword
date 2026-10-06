using System;
using UnityEngine;

namespace AudioTool
{
    [Serializable]
    internal sealed class AudioEditSession : ScriptableObject
    {
        [SerializeField] private string _materializedPath;
        [SerializeField] private string _fileName;
        [SerializeField] private string _projectPath;
        [SerializeField] private string _exportFolder;
        [SerializeField] private bool _isInProject;

        [SerializeField] private bool _hasSelection;
        [SerializeField] private float _selectionStart;
        [SerializeField] private float _selectionEnd = 1f;
        [SerializeField] private float _playhead;
        [SerializeField] private float _zoom = 1f;
        [SerializeField] private float _viewportStart;
        [SerializeField] private bool _loop = true;
        [SerializeField] private bool _playSelection = true;

        [SerializeField] private bool _normalize;
        [SerializeField] private float _normalizeTarget = 0.95f;
        [SerializeField] private bool _fadeIn;
        [SerializeField] private float _fadeInDuration = 1f;
        [SerializeField] private AnimationCurve _fadeInCurve = null;
        [SerializeField] private bool _fadeOut;
        [SerializeField] private float _fadeOutDuration = 1f;
        [SerializeField] private AnimationCurve _fadeOutCurve = null;
        [SerializeField] private bool _adjustVolume;
        [SerializeField] private float _volume = 1f;
        [SerializeField] private float _silenceThreshold = 0.01f;
        [SerializeField] private bool _processingExpanded = true;
        [SerializeField] private bool _advancedExpanded;

        internal string MaterializedPath { get => _materializedPath; set => _materializedPath = value; }
        internal string FileName { get => _fileName; set => _fileName = value; }
        internal string ProjectPath { get => _projectPath; set => _projectPath = value; }
        internal string ExportFolder { get => _exportFolder; set => _exportFolder = value; }
        internal bool IsInProject { get => _isInProject; set => _isInProject = value; }
        internal bool HasSelection { get => _hasSelection; set => _hasSelection = value; }
        internal float SelectionStart { get => _selectionStart; set => _selectionStart = Mathf.Clamp01(value); }
        internal float SelectionEnd { get => _selectionEnd; set => _selectionEnd = Mathf.Clamp01(value); }
        internal float Playhead { get => _playhead; set => _playhead = Mathf.Clamp01(value); }
        internal float Zoom { get => _zoom; set => _zoom = Mathf.Clamp(value, 1f, 32f); }
        internal float ViewportStart { get => _viewportStart; set => _viewportStart = Mathf.Clamp01(value); }
        internal bool Loop { get => _loop; set => _loop = value; }
        internal bool PlaySelection { get => _playSelection; set => _playSelection = value; }
        internal bool Normalize { get => _normalize; set => _normalize = value; }
        internal float NormalizeTarget { get => _normalizeTarget; set => _normalizeTarget = Mathf.Clamp01(value); }
        internal bool FadeIn { get => _fadeIn; set => _fadeIn = value; }
        internal float FadeInDuration { get => _fadeInDuration; set => _fadeInDuration = Mathf.Max(0.001f, value); }
        internal AnimationCurve FadeInCurve { get => _fadeInCurve ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f); set => _fadeInCurve = value; }
        internal bool FadeOut { get => _fadeOut; set => _fadeOut = value; }
        internal float FadeOutDuration { get => _fadeOutDuration; set => _fadeOutDuration = Mathf.Max(0.001f, value); }
        internal AnimationCurve FadeOutCurve { get => _fadeOutCurve ??= AnimationCurve.EaseInOut(0f, 1f, 1f, 0f); set => _fadeOutCurve = value; }
        internal bool AdjustVolume { get => _adjustVolume; set => _adjustVolume = value; }
        internal float Volume { get => _volume; set => _volume = Mathf.Clamp(value, 0f, 2f); }
        internal float SilenceThreshold { get => _silenceThreshold; set => _silenceThreshold = Mathf.Clamp(value, 0.0001f, 1f); }
        internal bool ProcessingExpanded { get => _processingExpanded; set => _processingExpanded = value; }
        internal bool AdvancedExpanded { get => _advancedExpanded; set => _advancedExpanded = value; }

        internal float ViewportDurationNormalized => 1f / Mathf.Max(1f, _zoom);

        internal void ClearSelection()
        {
            _hasSelection = false;
            _selectionStart = 0f;
            _selectionEnd = 1f;
        }

        internal void ResetEdits()
        {
            _hasSelection = false;
            _selectionStart = 0f;
            _selectionEnd = 1f;
            _playhead = 0f;
            _zoom = 1f;
            _viewportStart = 0f;
            _loop = true;
            _playSelection = true;
            _normalize = false;
            _normalizeTarget = 0.95f;
            _fadeIn = false;
            _fadeInDuration = 1f;
            _fadeOut = false;
            _fadeOutDuration = 1f;
            _fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            _fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            _adjustVolume = false;
            _volume = 1f;
            _silenceThreshold = 0.01f;
        }

        internal void NormalizeSelection(float minimumWidth = 0.000001f)
        {
            _selectionStart = Mathf.Clamp01(_selectionStart);
            _selectionEnd = Mathf.Clamp01(_selectionEnd);
            if (_selectionStart > _selectionEnd)
            {
                float swap = _selectionStart;
                _selectionStart = _selectionEnd;
                _selectionEnd = swap;
            }

            if (_hasSelection && _selectionEnd - _selectionStart < minimumWidth)
            {
                _selectionEnd = Mathf.Min(1f, _selectionStart + minimumWidth);
                _selectionStart = Mathf.Max(0f, _selectionEnd - minimumWidth);
            }

            ClampViewport();
        }

        internal void ClampViewport()
        {
            float maximumStart = Mathf.Max(0f, 1f - ViewportDurationNormalized);
            _viewportStart = Mathf.Clamp(_viewportStart, 0f, maximumStart);
        }

        internal EditSnapshot CaptureEdits()
        {
            return new EditSnapshot(
                _hasSelection, _selectionStart, _selectionEnd, _playhead, _zoom, _viewportStart, _loop, _playSelection,
                _normalize, _normalizeTarget, _fadeIn, _fadeInDuration, FadeInCurve, _fadeOut, _fadeOutDuration, FadeOutCurve,
                _adjustVolume, _volume, _silenceThreshold, _processingExpanded, _advancedExpanded);
        }

        internal void RestoreEdits(EditSnapshot snapshot)
        {
            _hasSelection = snapshot.HasSelection;
            _selectionStart = snapshot.SelectionStart;
            _selectionEnd = snapshot.SelectionEnd;
            _playhead = snapshot.Playhead;
            _zoom = snapshot.Zoom;
            _viewportStart = snapshot.ViewportStart;
            _loop = snapshot.Loop;
            _playSelection = snapshot.PlaySelection;
            _normalize = snapshot.Normalize;
            _normalizeTarget = snapshot.NormalizeTarget;
            _fadeIn = snapshot.FadeIn;
            _fadeInDuration = snapshot.FadeInDuration;
            _fadeInCurve = snapshot.FadeInCurve;
            _fadeOut = snapshot.FadeOut;
            _fadeOutDuration = snapshot.FadeOutDuration;
            _fadeOutCurve = snapshot.FadeOutCurve;
            _adjustVolume = snapshot.AdjustVolume;
            _volume = snapshot.Volume;
            _silenceThreshold = snapshot.SilenceThreshold;
            _processingExpanded = snapshot.ProcessingExpanded;
            _advancedExpanded = snapshot.AdvancedExpanded;
            NormalizeSelection();
        }

        internal readonly struct EditSnapshot
        {
            internal EditSnapshot(
                bool hasSelection, float selectionStart, float selectionEnd, float playhead, float zoom, float viewportStart,
                bool loop, bool playSelection, bool normalize, float normalizeTarget, bool fadeIn, float fadeInDuration,
                AnimationCurve fadeInCurve, bool fadeOut, float fadeOutDuration, AnimationCurve fadeOutCurve,
                bool adjustVolume, float volume, float silenceThreshold, bool processingExpanded, bool advancedExpanded)
            {
                HasSelection = hasSelection;
                SelectionStart = selectionStart;
                SelectionEnd = selectionEnd;
                Playhead = playhead;
                Zoom = zoom;
                ViewportStart = viewportStart;
                Loop = loop;
                PlaySelection = playSelection;
                Normalize = normalize;
                NormalizeTarget = normalizeTarget;
                FadeIn = fadeIn;
                FadeInDuration = fadeInDuration;
                FadeInCurve = fadeInCurve == null ? null : new AnimationCurve(fadeInCurve.keys);
                FadeOut = fadeOut;
                FadeOutDuration = fadeOutDuration;
                FadeOutCurve = fadeOutCurve == null ? null : new AnimationCurve(fadeOutCurve.keys);
                AdjustVolume = adjustVolume;
                Volume = volume;
                SilenceThreshold = silenceThreshold;
                ProcessingExpanded = processingExpanded;
                AdvancedExpanded = advancedExpanded;
            }

            internal bool HasSelection { get; }
            internal float SelectionStart { get; }
            internal float SelectionEnd { get; }
            internal float Playhead { get; }
            internal float Zoom { get; }
            internal float ViewportStart { get; }
            internal bool Loop { get; }
            internal bool PlaySelection { get; }
            internal bool Normalize { get; }
            internal float NormalizeTarget { get; }
            internal bool FadeIn { get; }
            internal float FadeInDuration { get; }
            internal AnimationCurve FadeInCurve { get; }
            internal bool FadeOut { get; }
            internal float FadeOutDuration { get; }
            internal AnimationCurve FadeOutCurve { get; }
            internal bool AdjustVolume { get; }
            internal float Volume { get; }
            internal float SilenceThreshold { get; }
            internal bool ProcessingExpanded { get; }
            internal bool AdvancedExpanded { get; }
        }
    }
}
