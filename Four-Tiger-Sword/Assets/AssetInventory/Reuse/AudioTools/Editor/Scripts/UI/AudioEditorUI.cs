using ImpossibleRobert.Common;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AudioTool
{
    /// <summary>
    /// Audio editor window for trimming, processing, and exporting audio files.
    /// Can work standalone (via context menu) or embedded (via IAudioSource).
    /// </summary>
    internal sealed class AudioEditorUI : CommonEditorUI
    {
        private const float CompactBreakpoint = 620f;
        private const float WideBreakpoint = 980f;

        [SerializeField] private AudioEditSession _session;

        private AudioEditorController _controller;
        private VisualElement _emptyState;
        private VisualElement _workspace;
        private VisualElement _workspaceBody;
        private AudioWaveformElement _waveform;
        private Label _sourceTitle;
        private Label _sourceDetails;
        private Label _sourcePeak;
        private Label _outputPeak;
        private Label _selectionSummary;
        private Label _destinationSummary;
        private Label _volumeDb;
        private Label _effectStatus;
        private HelpBox _statusBox;
        private HelpBox _errorBox;
        private HelpBox _clippingBox;
        private Button _playButton;
        private Button _clearSelectionButton;
        private Button _replaceButton;
        private Button _primaryExportButton;
        private Toggle _selectionPlaybackToggle;
        private bool _synchronizing;
        private bool _restoreRequested;

        public static AudioEditorUI ShowWindow()
        {
            AudioEditorUI window = GetWindow<AudioEditorUI>();
            window.titleContent = new GUIContent("Audio Editor", EditorGUIUtility.IconContent("AudioClip Icon").image);
            window.minSize = new Vector2(430f, 300f);
            window.Show();
            return window;
        }

        /// <summary>
        /// Initializes the editor with an audio source.
        /// </summary>
        /// <param name="audioSource">The audio source to edit.</param>
        /// <param name="exportFolder">Target folder for export (defaults to the source folder).</param>
        public async void Init(IAudioSource audioSource, string exportFolder = null)
        {
            EnsureController();
            await _controller.InitializeAsync(audioSource, exportFolder, true);
            BindWaveform();
            RefreshView();
        }

        private void OnEnable()
        {
            EnsureController();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (_controller != null)
            {
                _controller.Changed -= RefreshView;
                _controller.Dispose();
                _controller = null;
            }
        }

        private void OnDestroy()
        {
            if (_session != null && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                DestroyImmediate(_session);
                _session = null;
            }
        }

        public void CreateGUI()
        {
            EnsureController();
            VisualElement root = rootVisualElement;
            root.Clear();
            root.name = "audio-tools-root";
            StyleSheet styleSheet = CommonUITK.LoadStyleSheetFromAnchor(
                "AudioEditorUI",
                "Editor/Scripts/UI/AudioEditorUI.cs",
                "Editor/Scripts/UI/AudioTools.uss");
            CommonUITK.ApplyRoot(root, styleSheet, "at-root", "at-theme-dark", "at-theme-light");
            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            _emptyState = BuildEmptyState();
            root.Add(_emptyState);
            _workspace = BuildWorkspace();
            root.Add(_workspace);
            RegisterDragAndDrop(root);

            root.schedule.Execute(() =>
            {
                if (_controller == null) return;
                _controller.TickPlayback();
                if (_controller.IsPlaying) _waveform?.Refresh();
            }).Every(33);

            BindWaveform();
            RefreshView();

            if (!_restoreRequested && !_controller.HasAudio && !string.IsNullOrEmpty(_session.MaterializedPath))
            {
                _restoreRequested = true;
                _ = RestoreAfterDomainReloadAsync();
            }
        }

        private async Task RestoreAfterDomainReloadAsync()
        {
            await _controller.RestoreAsync();
            BindWaveform();
            RefreshView();
        }

        private void EnsureController()
        {
            if (_session == null)
            {
                _session = CreateInstance<AudioEditSession>();
                _session.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_controller != null) return;
            _controller = new AudioEditorController(_session);
            _controller.Changed += RefreshView;
        }

        private VisualElement BuildEmptyState()
        {
            CommonEmptyState state = new CommonEmptyState(new CommonEmptyState.EmptyStateClasses
            {
                RootClass = "at-empty-state",
                IconClass = "at-empty-logo",
                TitleClass = "at-empty-title",
                DetailClass = "at-empty-description",
                ActionsClass = "at-empty-actions"
            });
            state.name = "audio-empty-state";

            Texture2D logo = CommonUIStyles.LoadTexture("AudioTools");
            Button browse = CommonUITK.CreateButton("Browse File...", BrowseForSource, "at-button", "at-primary-button");
            browse.tooltip = "Choose a supported audio file from disk.";
            Button help = CommonUITK.CreateButton("How it works", AudioToolsWelcomeWindow.ShowWindow, "at-button");
            help.tooltip = "Open the Audio Tools quick-start guide.";
            state.SetContent(
                "Shape audio without leaving Unity",
                "Choose one audio file, select the part you need, preview the result, and export a new WAV. Your source stays untouched until you explicitly replace it.",
                logo,
                new VisualElement[] { browse, help });

            ObjectField clipField = new ObjectField("Audio asset")
            {
                name = "audio-source-field",
                objectType = typeof(AudioClip),
                allowSceneObjects = false,
                tooltip = "Select an AudioClip from this project."
            };
            clipField.AddToClassList("at-source-field");
            clipField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is AudioClip clip) LoadProjectClip(clip);
            });
            state.Insert(state.IndexOf(state.Actions), clipField);
            state.Add(CommonUITK.CreateLabel("You can also drag one WAV, MP3, OGG, AIFF, AIF, or FLAC file here.", "at-drop-hint"));

            _errorBox = CommonUITK.CreateHelpBox(string.Empty, HelpBoxMessageType.Error, "at-message");
            state.Add(_errorBox);
            return state;
        }

        private VisualElement BuildWorkspace()
        {
            VisualElement root = CommonUITK.CreateContainer("at-workspace");
            root.name = "audio-workspace";
            root.Add(BuildSourceHeader());

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "audio-workspace-scroll",
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            scroll.AddToClassList("at-scroll");
            root.Add(scroll);

            _workspaceBody = CommonUITK.CreateContainer("at-workspace-body");
            scroll.Add(_workspaceBody);

            VisualElement editing = CommonUITK.CreateContainer("at-editing-column");
            editing.Add(BuildWaveformCard());
            editing.Add(BuildTransportCard());
            _workspaceBody.Add(editing);
            _workspaceBody.Add(BuildProcessingPanel());

            root.Add(BuildExportFooter());
            return root;
        }

        private VisualElement BuildSourceHeader()
        {
            VisualElement header = CommonUITK.CreateContainer("at-source-header");
            VisualElement identity = CommonUITK.CreateContainer("at-source-identity");
            _sourceTitle = CommonUITK.CreateLabel(string.Empty, "at-source-title");
            _sourceDetails = CommonUITK.CreateLabel(string.Empty, "at-source-details");
            identity.Add(_sourceTitle);
            identity.Add(_sourceDetails);
            header.Add(identity);

            VisualElement metrics = CommonUITK.CreateContainer("at-metrics");
            _sourcePeak = CreateMetric(metrics, "SOURCE PEAK");
            _outputPeak = CreateMetric(metrics, "OUTPUT PEAK");
            header.Add(metrics);
            return header;
        }

        private static Label CreateMetric(VisualElement parent, string title)
        {
            VisualElement metric = CommonUITK.CreateContainer("at-metric");
            metric.Add(CommonUITK.CreateLabel(title, "at-metric-label"));
            Label value = CommonUITK.CreateLabel("-", "at-metric-value");
            metric.Add(value);
            parent.Add(metric);
            return value;
        }

        private VisualElement BuildWaveformCard()
        {
            VisualElement card = CreateCard("Waveform", "Drag to select, click to seek, use Ctrl/Cmd + wheel to zoom, and right-drag to pan.");
            _waveform = new AudioWaveformElement();
            _waveform.EditGestureStarted += () => Undo.RecordObject(_session, "Edit audio selection");
            _waveform.EditGestureFinished += () =>
            {
                EditorUtility.SetDirty(_session);
                _controller.ScheduleProcessing();
                RefreshView();
            };
            _waveform.SelectionChanged += RefreshView;
            _waveform.SeekRequested += _controller.Seek;
            _waveform.ViewportChanged += RefreshView;
            card.Add(_waveform);
            return card;
        }

        private VisualElement BuildTransportCard()
        {
            VisualElement card = CreateCard("Preview & selection", "Use the waveform handles to choose and preview the exact region you need.");
            VisualElement transport = CommonUITK.CreateContainer("at-transport");
            _playButton = CommonUITK.CreateButton("Play", _controller.TogglePlayback, "at-button", "at-transport-button", "at-primary-button");
            transport.Add(_playButton);
            transport.Add(CommonUITK.CreateButton("Rewind", () =>
            {
                _controller.Stop();
                _controller.Seek(_session.HasSelection && _session.PlaySelection ? _session.SelectionStart : 0f);
            }, "at-button", "at-quiet-button"));
            Toggle loop = new Toggle("Loop") { name = "audio-loop" };
            loop.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Toggle audio loop");
                _session.Loop = evt.newValue;
            });
            transport.Add(loop);
            _selectionPlaybackToggle = new Toggle("Selection only") { name = "audio-selection-playback" };
            _selectionPlaybackToggle.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change preview region");
                _session.PlaySelection = evt.newValue;
            });
            transport.Add(_selectionPlaybackToggle);
            card.Add(transport);

            VisualElement quickActions = CommonUITK.CreateContainer("at-quick-actions");
            quickActions.Add(CommonUITK.CreateButton("Select Audible Content", SelectAudibleContent, "at-button"));
            quickActions.Add(CommonUITK.CreateButton("Select All", SelectAll, "at-button", "at-quiet-button"));
            _clearSelectionButton = CommonUITK.CreateButton("Clear Selection", ClearSelection, "at-button", "at-quiet-button");
            quickActions.Add(_clearSelectionButton);
            card.Add(quickActions);
            return card;
        }

        private VisualElement BuildProcessingPanel()
        {
            VisualElement panel = CommonUITK.CreateContainer("at-processing-panel");
            panel.name = "audio-processing-panel";
            Foldout processing = CommonUITK.CreateFoldout("Enhance", _session.ProcessingExpanded, value => _session.ProcessingExpanded = value,
                "Processing is non-destructive until export.", "at-processing-foldout");
            panel.Add(processing);
            processing.Add(BuildNormalizeEffect());
            processing.Add(BuildFadeEffect(true));
            processing.Add(BuildFadeEffect(false));
            processing.Add(BuildVolumeEffect());

            Foldout advanced = CommonUITK.CreateFoldout("Advanced", _session.AdvancedExpanded, value => _session.AdvancedExpanded = value,
                "Precision controls for silence detection and session reset.", "at-advanced-foldout");
            panel.Add(advanced);
            Slider silence = new Slider("Silence threshold (dB)", -80f, -6f) { showInputField = true };
            silence.name = "audio-silence-threshold";
            silence.SetValueWithoutNotify(AmplitudeToDb(_session.SilenceThreshold));
            silence.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change silence threshold");
                _session.SilenceThreshold = DbToAmplitude(evt.newValue);
            });
            advanced.Add(silence);
            advanced.Add(CommonUITK.CreateButton("Reset editing session", ResetSession, "at-button", "at-quiet-button"));
            _effectStatus = CommonUITK.CreateLabel(string.Empty, "at-effect-status");
            panel.Add(_effectStatus);
            return panel;
        }

        private VisualElement BuildNormalizeEffect()
        {
            VisualElement effect = CreateEffectCard("Normalize", "Raise or lower the selected region to a precise peak level.");
            Toggle enabled = new Toggle("Enable normalize") { name = "audio-normalize" };
            Slider target = new Slider("Target peak (dB)", -24f, 0f) { showInputField = true, name = "audio-normalize-target" };
            enabled.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Toggle normalization");
                _session.Normalize = evt.newValue;
                target.SetEnabled(evt.newValue);
                _controller.ScheduleProcessing();
                RefreshView();
            });
            target.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change normalize target");
                _session.NormalizeTarget = DbToAmplitude(evt.newValue);
                _controller.ScheduleProcessing();
            });
            effect.Add(enabled);
            effect.Add(target);
            return effect;
        }

        private VisualElement BuildFadeEffect(bool fadeIn)
        {
            string title = fadeIn ? "Fade In" : "Fade Out";
            VisualElement effect = CreateEffectCard(title, fadeIn ? "Bring the region in smoothly." : "Finish the region cleanly.");
            Toggle enabled = new Toggle("Enable " + title.ToLowerInvariant()) { name = fadeIn ? "audio-fade-in" : "audio-fade-out" };
            FloatField duration = new FloatField("Duration (seconds)") { name = fadeIn ? "audio-fade-in-duration" : "audio-fade-out-duration" };
            CurveField curve = new CurveField("Curve") { name = fadeIn ? "audio-fade-in-curve" : "audio-fade-out-curve" };
            enabled.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Toggle " + title.ToLowerInvariant());
                if (fadeIn) _session.FadeIn = evt.newValue;
                else _session.FadeOut = evt.newValue;
                duration.SetEnabled(evt.newValue);
                curve.SetEnabled(evt.newValue);
                _controller.ScheduleProcessing();
                RefreshView();
            });
            duration.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change " + title.ToLowerInvariant() + " duration");
                float clamped = Mathf.Clamp(evt.newValue, 0.001f, Mathf.Max(0.001f, _controller.GetSelectionDuration()));
                if (fadeIn) _session.FadeInDuration = clamped;
                else _session.FadeOutDuration = clamped;
                _controller.ScheduleProcessing();
                RefreshView();
            });
            curve.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change " + title.ToLowerInvariant() + " curve");
                if (fadeIn) _session.FadeInCurve = evt.newValue;
                else _session.FadeOutCurve = evt.newValue;
                _controller.ScheduleProcessing();
                _waveform.Refresh();
            });
            effect.Add(enabled);
            effect.Add(duration);
            effect.Add(curve);
            return effect;
        }

        private VisualElement BuildVolumeEffect()
        {
            VisualElement effect = CreateEffectCard("Volume", "Scale loudness while keeping samples within the WAV range.");
            Toggle enabled = new Toggle("Enable volume adjustment") { name = "audio-volume" };
            Slider amount = new Slider("Amount", 0f, 2f) { showInputField = true, name = "audio-volume-amount" };
            _volumeDb = CommonUITK.CreateLabel("100% · 0.0 dB", "at-secondary-value");
            enabled.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Toggle volume adjustment");
                _session.AdjustVolume = evt.newValue;
                amount.SetEnabled(evt.newValue);
                _controller.ScheduleProcessing();
                RefreshView();
            });
            amount.RegisterValueChangedCallback(evt =>
            {
                if (_synchronizing) return;
                RecordSession("Change volume");
                _session.Volume = evt.newValue;
                _controller.ScheduleProcessing();
                RefreshView();
            });
            effect.Add(enabled);
            effect.Add(amount);
            effect.Add(_volumeDb);
            return effect;
        }

        private VisualElement BuildExportFooter()
        {
            VisualElement footer = CommonUITK.CreateContainer("at-export-footer");
            footer.name = "audio-export-footer";
            VisualElement summary = CommonUITK.CreateContainer("at-export-summary");
            _selectionSummary = CommonUITK.CreateLabel(string.Empty, "at-export-title");
            _destinationSummary = CommonUITK.CreateLabel(string.Empty, "at-export-details");
            summary.Add(_selectionSummary);
            summary.Add(_destinationSummary);
            footer.Add(summary);

            _statusBox = CommonUITK.CreateHelpBox(string.Empty, HelpBoxMessageType.Info, "at-message", "at-footer-message");
            footer.Add(_statusBox);
            _clippingBox = CommonUITK.CreateHelpBox(string.Empty, HelpBoxMessageType.Warning, "at-message", "at-footer-message");
            footer.Add(_clippingBox);

            VisualElement actions = CommonUITK.CreateContainer("at-export-actions");
            _replaceButton = CommonUITK.CreateButton("Replace Original WAV", ReplaceOriginal, "at-button", "at-danger-button");
            _replaceButton.style.display = DisplayStyle.None;
            _primaryExportButton = CommonUITK.CreateButton("Save Copy", SavePrimary, "at-button", "at-primary-button");
            actions.Add(_replaceButton);
            actions.Add(_primaryExportButton);
            footer.Add(actions);
            return footer;
        }

        private static VisualElement CreateCard(string title, string description)
        {
            VisualElement card = CommonUITK.CreateContainer("at-card");
            card.Add(CommonUITK.CreateLabel(title, "at-card-title"));
            card.Add(CommonUITK.CreateLabel(description, "at-card-description"));
            return card;
        }

        private static VisualElement CreateEffectCard(string title, string description)
        {
            VisualElement card = CommonUITK.CreateContainer("at-effect-card");
            card.Add(CommonUITK.CreateLabel(title, "at-effect-title"));
            card.Add(CommonUITK.CreateLabel(description, "at-effect-description"));
            return card;
        }

        private void RefreshView()
        {
            if (rootVisualElement == null || _emptyState == null || _workspace == null || _controller == null) return;
            _synchronizing = true;
            try
            {
                bool hasAudio = _controller.HasAudio;
                _emptyState.style.display = hasAudio || _controller.IsLoading ? DisplayStyle.None : DisplayStyle.Flex;
                _workspace.style.display = hasAudio || _controller.IsLoading ? DisplayStyle.Flex : DisplayStyle.None;
                _errorBox.text = _controller.Error ?? string.Empty;
                _errorBox.style.display = string.IsNullOrEmpty(_controller.Error) ? DisplayStyle.None : DisplayStyle.Flex;

                if (!hasAudio)
                {
                    if (_controller.IsLoading)
                    {
                        _workspace.style.display = DisplayStyle.Flex;
                        _sourceTitle.text = _session.FileName ?? "Loading audio";
                        _sourceDetails.text = _controller.Status ?? "Preparing source...";
                    }
                    return;
                }

                AudioClip clip = _controller.SourceClip;
                _sourceTitle.text = _session.FileName ?? clip.name;
                _sourceDetails.text = FormatTime(clip.length) + "  ·  " + (clip.channels == 1 ? "Mono" : clip.channels + " channels") + "  ·  " + clip.frequency + " Hz";
                _sourcePeak.text = FormatPeak(_controller.SourcePeak);
                _outputPeak.text = FormatPeak(_controller.OutputPeak);
                _playButton.text = _controller.IsPlaying ? "Stop" : "Play";
                _selectionPlaybackToggle.SetValueWithoutNotify(_session.PlaySelection);
                _selectionPlaybackToggle.SetEnabled(_session.HasSelection);
                _clearSelectionButton.SetEnabled(_session.HasSelection);

                _selectionSummary.text = (_session.HasSelection ? "Selection" : "Full clip") + " · " + FormatTime(_controller.GetSelectionDuration()) + " · WAV / 16-bit PCM";
                string destination = _controller.GetDefaultOutputPath(false);
                _destinationSummary.text = string.IsNullOrEmpty(destination) ? "Choose a destination when saving." : "Next output: " + GetCompactPath(destination);
                _destinationSummary.tooltip = destination ?? string.Empty;
                SetDisplayed(_replaceButton, CanReplaceOriginal());
                _primaryExportButton.text = GetPrimaryExportLabel();
                _statusBox.text = _controller.Status ?? string.Empty;
                _statusBox.style.display = string.IsNullOrEmpty(_controller.Status) || _controller.Status == "Ready" ? DisplayStyle.None : DisplayStyle.Flex;
                _clippingBox.text = _controller.ClippedSampleCount > 0
                    ? _controller.ClippedSampleCount + " samples reach the clipping limit. You can still export, but lowering volume may sound cleaner."
                    : string.Empty;
                _clippingBox.style.display = _controller.ClippedSampleCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _volumeDb.text = Mathf.RoundToInt(_session.Volume * 100f) + "% · " + FormatDb(_session.Volume);
                _effectStatus.text = GetEffectStatus();
                SyncEffectControls();
                BindWaveform();
                _waveform.Refresh();
            }
            finally
            {
                _synchronizing = false;
            }
        }

        private void SyncEffectControls()
        {
            Toggle normalize = rootVisualElement.Q<Toggle>("audio-normalize");
            Slider normalizeTarget = rootVisualElement.Q<Slider>("audio-normalize-target");
            Toggle fadeIn = rootVisualElement.Q<Toggle>("audio-fade-in");
            FloatField fadeInDuration = rootVisualElement.Q<FloatField>("audio-fade-in-duration");
            CurveField fadeInCurve = rootVisualElement.Q<CurveField>("audio-fade-in-curve");
            Toggle fadeOut = rootVisualElement.Q<Toggle>("audio-fade-out");
            FloatField fadeOutDuration = rootVisualElement.Q<FloatField>("audio-fade-out-duration");
            CurveField fadeOutCurve = rootVisualElement.Q<CurveField>("audio-fade-out-curve");
            Toggle volume = rootVisualElement.Q<Toggle>("audio-volume");
            Slider volumeAmount = rootVisualElement.Q<Slider>("audio-volume-amount");
            Toggle loop = rootVisualElement.Q<Toggle>("audio-loop");

            normalize?.SetValueWithoutNotify(_session.Normalize);
            normalizeTarget?.SetValueWithoutNotify(AmplitudeToDb(_session.NormalizeTarget));
            normalizeTarget?.SetEnabled(_session.Normalize);
            SetDisplayed(normalizeTarget, _session.Normalize);
            fadeIn?.SetValueWithoutNotify(_session.FadeIn);
            fadeInDuration?.SetValueWithoutNotify(_session.FadeInDuration);
            fadeInDuration?.SetEnabled(_session.FadeIn);
            SetDisplayed(fadeInDuration, _session.FadeIn);
            fadeInCurve?.SetValueWithoutNotify(_session.FadeInCurve);
            fadeInCurve?.SetEnabled(_session.FadeIn);
            SetDisplayed(fadeInCurve, _session.FadeIn);
            fadeOut?.SetValueWithoutNotify(_session.FadeOut);
            fadeOutDuration?.SetValueWithoutNotify(_session.FadeOutDuration);
            fadeOutDuration?.SetEnabled(_session.FadeOut);
            SetDisplayed(fadeOutDuration, _session.FadeOut);
            fadeOutCurve?.SetValueWithoutNotify(_session.FadeOutCurve);
            fadeOutCurve?.SetEnabled(_session.FadeOut);
            SetDisplayed(fadeOutCurve, _session.FadeOut);
            volume?.SetValueWithoutNotify(_session.AdjustVolume);
            volumeAmount?.SetValueWithoutNotify(_session.Volume);
            volumeAmount?.SetEnabled(_session.AdjustVolume);
            SetDisplayed(volumeAmount, _session.AdjustVolume);
            SetDisplayed(_volumeDb, _session.AdjustVolume);
            loop?.SetValueWithoutNotify(_session.Loop);
        }

        private static void SetDisplayed(VisualElement element, bool displayed)
        {
            if (element != null) element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BindWaveform()
        {
            _waveform?.Bind(_session, _controller?.PeakCache, _controller?.SourceClip == null ? 1f : _controller.SourceClip.length);
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyResponsiveState(evt.newRect.width);
        }

        internal void ApplyResponsiveState(float width)
        {
            if (_workspaceBody == null) return;
            _workspaceBody.EnableInClassList("at-compact", width < CompactBreakpoint);
            _workspaceBody.EnableInClassList("at-standard", width >= CompactBreakpoint && width < WideBreakpoint);
            _workspaceBody.EnableInClassList("at-wide", width >= WideBreakpoint);
            _workspace?.EnableInClassList("at-compact", width < CompactBreakpoint);
            _workspace?.EnableInClassList("at-standard", width >= CompactBreakpoint && width < WideBreakpoint);
            _workspace?.EnableInClassList("at-wide", width >= WideBreakpoint);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!_controller.HasAudio || IsEditingField(evt.target as VisualElement)) return;
            bool handled = true;
            switch (evt.keyCode)
            {
                case KeyCode.Space:
                    _controller.TogglePlayback();
                    break;
                case KeyCode.Escape:
                    ClearSelection();
                    break;
                case KeyCode.Home:
                    _controller.Seek(0f);
                    break;
                case KeyCode.End:
                    _controller.Seek(1f);
                    break;
                case KeyCode.A when evt.actionKey:
                    SelectAll();
                    break;
                case KeyCode.LeftArrow:
                    NudgeActivePosition(-1f, evt);
                    break;
                case KeyCode.RightArrow:
                    NudgeActivePosition(1f, evt);
                    break;
                default:
                    handled = false;
                    break;
            }
            if (handled) evt.StopPropagation();
        }

        private void NudgeActivePosition(float direction, KeyDownEvent evt)
        {
            float delta;
            if (evt.altKey) delta = 1f / Mathf.Max(1, _controller.SourceClip.samples);
            else delta = _waveform.PixelDeltaToNormalized(evt.shiftKey ? 10f : 1f);
            RecordSession("Nudge audio position");
            _controller.Seek(_session.Playhead + direction * delta);
        }

        private static bool IsEditingField(VisualElement target)
        {
            return target is TextField || target is FloatField || target is Slider || target is CurveField || target is ObjectField;
        }

        private void SelectAll()
        {
            RecordSession("Select full audio clip");
            _session.HasSelection = true;
            _session.SelectionStart = 0f;
            _session.SelectionEnd = 1f;
            _session.Playhead = 0f;
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private void ClearSelection()
        {
            if (!_session.HasSelection) return;
            RecordSession("Clear audio selection");
            _session.ClearSelection();
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private void SelectAudibleContent()
        {
            RecordSession("Select audible content");
            if (!_controller.SelectAudibleContent(out string message))
            {
                _effectStatus.text = message ?? "No audible region was found.";
                return;
            }
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private void ResetSession()
        {
            RecordSession("Reset Audio Tools session");
            _controller.Stop();
            _session.ResetEdits();
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private async void SavePrimary()
        {
            try
            {
                string path = _controller.GetDefaultOutputPath(false);
                if (RequiresSavePanel(path))
                {
                    string defaultName = Path.GetFileNameWithoutExtension(_session.FileName ?? "audio") + ".wav";
                    path = EditorUtility.SaveFilePanel("Save Audio as WAV", Path.GetDirectoryName(path) ?? string.Empty, defaultName, "wav");
                }
                if (string.IsNullOrEmpty(path)) return;
                _controller.Export(path);
                RefreshView();
                await Task.Yield();
            }
            catch (Exception exception)
            {
                ShowExportError(exception);
            }
        }

        private async void ReplaceOriginal()
        {
            if (!CanReplaceOriginal()) return;
            string path = _controller.GetDefaultOutputPath(true);
            bool confirmed = EditorUtility.DisplayDialog(
                "Replace Original WAV?",
                "This writes the current result to:\n\n" + path + "\n\nThe operation cannot be undone by Audio Tools.",
                "Replace WAV",
                "Cancel");
            if (!confirmed) return;
            try
            {
                _controller.Export(path);
                await _controller.InitializeAsync(new FileAudioSource(path), Path.GetDirectoryName(path), false);
                BindWaveform();
                RefreshView();
            }
            catch (Exception exception)
            {
                ShowExportError(exception);
            }
        }

        private void ShowExportError(Exception exception)
        {
            _statusBox.messageType = HelpBoxMessageType.Error;
            _statusBox.text = "Export failed: " + exception.Message;
            _statusBox.style.display = DisplayStyle.Flex;
            Debug.LogError("Audio Tools export failed: " + exception);
        }

        private bool CanReplaceOriginal()
        {
            return _session.IsInProject && !string.IsNullOrEmpty(_session.FileName) && _session.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        }

        private string GetPrimaryExportLabel()
        {
            if (_session.IsInProject) return "Save Copy";
            return IsInsideProjectAssets(_session.ExportFolder) ? "Import WAV" : "Save As WAV...";
        }

        private bool RequiresSavePanel(string path)
        {
            return !_session.IsInProject && !IsInsideProjectAssets(_session.ExportFolder) || string.IsNullOrEmpty(path);
        }

        private static bool IsInsideProjectAssets(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string fullFolder = Path.GetFullPath(folder).Replace('\\', '/');
            string dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return fullFolder.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase);
        }

        private void LoadProjectClip(AudioClip clip)
        {
            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath)) return;
            string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
            Init(new FileAudioSource(fullPath), Path.GetDirectoryName(fullPath));
        }

        private void BrowseForSource()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Choose Audio File",
                string.IsNullOrEmpty(_session.MaterializedPath) ? Application.dataPath : Path.GetDirectoryName(_session.MaterializedPath),
                new[] { "Audio files", "wav,mp3,ogg,aiff,aif,flac", "All files", "*" });
            if (string.IsNullOrEmpty(path)) return;
            Init(new FileAudioSource(path));
        }

        private void RegisterDragAndDrop(VisualElement root)
        {
            root.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (GetSingleDroppedAudioPath() == null) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            root.RegisterCallback<DragPerformEvent>(evt =>
            {
                string path = GetSingleDroppedAudioPath();
                if (path == null) return;
                DragAndDrop.AcceptDrag();
                string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
                Init(new FileAudioSource(fullPath));
                evt.StopPropagation();
            });
        }

        private static string GetSingleDroppedAudioPath()
        {
            if (DragAndDrop.paths == null || DragAndDrop.paths.Length != 1) return null;
            string path = DragAndDrop.paths[0];
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".wav" || extension == ".mp3" || extension == ".ogg" || extension == ".aiff" || extension == ".aif" || extension == ".flac" ? path : null;
        }

        private void OnUndoRedo()
        {
            _session.NormalizeSelection();
            _controller.ScheduleProcessing();
            RefreshView();
        }

        private void RecordSession(string name)
        {
            Undo.RecordObject(_session, name);
            EditorUtility.SetDirty(_session);
        }

        private string GetEffectStatus()
        {
            if (!_controller.HasAudio) return string.Empty;
            float regionDuration = _controller.GetSelectionDuration();
            bool clamped = _session.FadeIn && _session.FadeInDuration > regionDuration || _session.FadeOut && _session.FadeOutDuration > regionDuration;
            if (clamped) return "Fade duration is shortened to fit the active region.";
            if (!_session.Normalize && !_session.FadeIn && !_session.FadeOut && !_session.AdjustVolume) return "No enhancements enabled. The source waveform is unchanged.";
            return "Enhancements preview automatically after each change.";
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int minutes = Mathf.FloorToInt(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            return minutes + ":" + remainder.ToString("00.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatPeak(float amplitude)
        {
            return Mathf.RoundToInt(amplitude * 100f) + "%  ·  " + FormatDb(amplitude);
        }

        private static string FormatDb(float amplitude)
        {
            return amplitude <= 0.000001f ? "-∞ dB" : (20f * Mathf.Log10(amplitude)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dB";
        }

        private static float AmplitudeToDb(float amplitude)
        {
            return amplitude <= 0.0001f ? -80f : 20f * Mathf.Log10(amplitude);
        }

        private static float DbToAmplitude(float decibels)
        {
            return Mathf.Pow(10f, decibels / 20f);
        }

        private static string GetCompactPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string normalized = path.Replace('\\', '/');
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(projectRoot) && normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(projectRoot.Length + 1);
            }
            if (normalized.Length <= 72) return normalized;
            string fileName = Path.GetFileName(normalized);
            string parent = Path.GetFileName(Path.GetDirectoryName(normalized));
            return ".../" + parent + "/" + fileName;
        }

    }
}
