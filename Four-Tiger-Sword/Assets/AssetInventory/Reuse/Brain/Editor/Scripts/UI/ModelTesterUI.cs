using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Brain
{
    /// <summary>
    /// Editor window for testing and comparing AI models.
    /// </summary>
    internal sealed class ModelTesterUI : CommonEditorUI
    {
        private const float ModelColumnWidth = 180f;
        private const float ImageColumnWidth = 220f;
        private const float HeaderHeight = 138f;
        private const float RowHeight = 154f;
        private const string BlipBackend = "[BLIP]";
        private const string RootClass = "brain-model-tester-root";
        private const string LightClass = "brain-model-tester-light";
        private const string CompactClass = "brain-model-tester-compact";

        private List<TestModel> _models;
        private List<TestImage> _testImages;
        private Dictionary<string, bool> _modelEnabled;
        private Dictionary<string, bool> _imageEnabled;
        private Dictionary<(string model, string image), string> _results;
        private Dictionary<(string model, string image), float> _cellTimes;
        private Dictionary<string, float> _modelTotalTimes;
        private bool _showPrompt;
        private bool _isRunning;
        private CancellationTokenSource _cts;
        private string _customPrompt;
        private string _imagePath;
        private Action<string> _onCustomPromptChanged;
        private bool _customPromptSeeded;
        private int _completedTests;
        private int _totalTests;
        private string _activeStatus;

        private CommonComparisonMatrix<TestModel, TestImage> _matrix;
        private VisualElement _root;
        private VisualElement _promptHost;
        private Label _statusLabel;
        private ProgressBar _progress;
        private Button _runAllButton;
        private Button _cancelButton;

        public static ModelTesterUI ShowWindow(string imagePath, string initialCustomPrompt = null, Action<string> onCustomPromptChanged = null)
        {
            ModelTesterUI ui = GetWindow<ModelTesterUI>("AI Model Tester");
            ui.minSize = new Vector2(680, 500);
            ui._imagePath = imagePath;
            ui._onCustomPromptChanged = onCustomPromptChanged;
            ui._customPrompt = string.IsNullOrEmpty(initialCustomPrompt) ? null : initialCustomPrompt;
            ui._customPromptSeeded = true;
            ui.Init();
            ui.BuildIfReady();
            return ui;
        }

        private void OnEnable()
        {
            Init();
        }

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _isRunning = false;
        }

        private void CreateGUI()
        {
            Build();
        }

        private void Init()
        {
            _testImages = GetTestImages() ?? new List<TestImage>();
            _models = GatherModels();
            _modelEnabled = _models.ToDictionary(model => model.Name, model => model.Name != BlipBackend);
            if (_models.Count == 1 && _models[0].Name == BlipBackend) _modelEnabled[BlipBackend] = true;
            _imageEnabled = _testImages.ToDictionary(image => image.path, _ => true);
            _results = new Dictionary<(string, string), string>();
            _cellTimes = new Dictionary<(string, string), float>();
            _modelTotalTimes = new Dictionary<string, float>();
            _isRunning = false;
            _completedTests = 0;
            _totalTests = 0;
            _activeStatus = null;
            _cts?.Dispose();
            _cts = null;
            if (!_customPromptSeeded) _customPrompt = null;
        }

        private List<TestModel> GatherModels()
        {
            List<TestModel> models = new List<TestModel>();
            int currentBackend = Intelligence.Settings.AIBackend;
            if (currentBackend == 1 && Intelligence.OllamaModels != null)
            {
                models.AddRange(Intelligence.OllamaModels
                    .OrderBy(model => model.Name, StringComparer.InvariantCultureIgnoreCase)
                    .Select(model => new TestModel {Name = model.Name, Backend = 1}));
            }
            else if (currentBackend == 2 && Intelligence.LMStudioModels != null)
            {
                models.AddRange(Intelligence.LMStudioModels
                    .Where(model => model.SupportsVision)
                    .OrderBy(model => model.id, StringComparer.InvariantCultureIgnoreCase)
                    .Select(model => new TestModel {Name = model.id, Backend = 2}));
            }

            if (currentBackend == 0 || models.Count > 0)
            {
                models.Add(new TestModel {Name = BlipBackend, Backend = 0});
            }
            return models;
        }

        private void BuildIfReady()
        {
            if (rootVisualElement != null && rootVisualElement.panel != null) Build();
        }

        private void Build()
        {
            _root = rootVisualElement;
            if (_root == null) return;

            _root.Clear();
            StyleSheet styleSheet = CommonUITK.LoadStyleSheetFromAnchor(
                nameof(ModelTesterUI),
                "/Editor/Scripts/UI/ModelTesterUI.cs",
                "/Editor/Scripts/UI/ModelTesterUI.uss");
            CommonUITK.ApplyRoot(_root, styleSheet, RootClass, null, LightClass);
            _root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _root.schedule.Execute(() => ApplyCompactState(_root.resolvedStyle.width));

            VisualElement header = CommonUITK.CreateContainer("brain-model-tester-header");
            header.Add(CommonUITK.CreateLabel("Compare Caption Models", "brain-model-tester-title"));
            header.Add(CommonUITK.CreateLabel(
                "Run the same images through available vision models and compare captions and response times.",
                "brain-model-tester-subtitle"));
            _root.Add(header);

            if (_models == null || _testImages == null || _models.Count == 0 || _testImages.Count == 0)
            {
                BuildEmptyState();
                return;
            }

            VisualElement body = CommonUITK.CreateContainer("brain-model-tester-body");
            _matrix = new CommonComparisonMatrix<TestModel, TestImage>(
                CreateCorner,
                CreateImageHeader,
                CreateModelHeader,
                CreateResultCell,
                ModelColumnWidth,
                ImageColumnWidth,
                HeaderHeight,
                RowHeight,
                new CommonComparisonMatrix<TestModel, TestImage>.MatrixClasses
                {
                    RootClass = "brain-model-tester-matrix",
                    ContentClass = "brain-model-tester-matrix-content",
                    HeaderClass = "brain-model-tester-matrix-header",
                    CornerClass = "brain-model-tester-corner",
                    ColumnHeaderClass = "brain-model-tester-column-header",
                    RowClass = "brain-model-tester-matrix-row",
                    RowHeaderClass = "brain-model-tester-row-header",
                    CellClass = "brain-model-tester-cell"
                });
            _matrix.SetItems(_models, _testImages);
            body.Add(_matrix);

            _promptHost = CommonUITK.CreateContainer();
            body.Add(_promptHost);
            BuildPrompt();
            _root.Add(body);
            BuildFooter();
            UpdateControls();
        }

        private void BuildEmptyState()
        {
            VisualElement empty = CommonUITK.CreateContainer("brain-model-tester-empty");
            string message = _testImages == null || _testImages.Count == 0
                ? "No PNG test images were found in the configured caption test folder."
                : "No compatible vision models are available. Make sure the selected Ollama or LM Studio service is running and reload the model list.";
            empty.Add(CommonUITK.CreateLabel(message, "brain-model-tester-empty-copy"));
            Button reload = CommonUITK.CreateButton("Reload Data", () =>
            {
                Init();
                Build();
            }, "brain-model-tester-action", "brain-model-tester-primary");
            empty.Add(reload);
            _root.Add(empty);
        }

        private VisualElement CreateCorner()
        {
            VisualElement corner = CommonUITK.CreateContainer("brain-model-tester-corner-content");
            corner.Add(CommonUITK.CreateLabel("Models", "brain-model-tester-corner-title"));
            corner.Add(CommonUITK.CreateLabel(
                $"{_models.Count} available / {_testImages.Count} test image{(_testImages.Count == 1 ? string.Empty : "s")}",
                "brain-model-tester-corner-copy"));
            return corner;
        }

        private VisualElement CreateImageHeader(TestImage image)
        {
            VisualElement header = CommonUITK.CreateContainer("brain-model-tester-image-header");
            Toggle enabled = new Toggle {value = _imageEnabled[image.path], tooltip = "Include this image in selected tests"};
            enabled.AddToClassList("brain-model-tester-image-toggle");
            enabled.RegisterValueChangedCallback(evt =>
            {
                _imageEnabled[image.path] = evt.newValue;
                ScheduleVisualUpdate(() =>
                {
                    _matrix?.RefreshColumn(image);
                    UpdateControls();
                });
            });
            header.Add(enabled);

            Image preview = new Image
            {
                image = image.texture,
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = image.path
            };
            preview.AddToClassList("brain-model-tester-image-preview");
            header.Add(preview);

            Label name = CommonUITK.CreateLabel(Path.GetFileNameWithoutExtension(image.path), "brain-model-tester-image-name");
            name.tooltip = image.path;
            header.Add(name);
            return header;
        }

        private VisualElement CreateModelHeader(TestModel model)
        {
            VisualElement header = CommonUITK.CreateContainer("brain-model-tester-model-header");
            VisualElement heading = CommonUITK.CreateContainer("brain-model-tester-model-heading");
            Toggle enabled = new Toggle {value = _modelEnabled[model.Name], tooltip = "Include this model in selected tests"};
            enabled.AddToClassList("brain-model-tester-model-toggle");
            enabled.RegisterValueChangedCallback(evt =>
            {
                _modelEnabled[model.Name] = evt.newValue;
                ScheduleVisualUpdate(() =>
                {
                    _matrix?.RefreshRow(model);
                    UpdateControls();
                });
            });
            heading.Add(enabled);
            Label modelName = CommonUITK.CreateLabel(model.Name, "brain-model-tester-model-name");
            modelName.tooltip = model.Name;
            heading.Add(modelName);
            header.Add(heading);

            string backend = GetBackendName(model.Backend);
            string timing = _modelTotalTimes.TryGetValue(model.Name, out float total)
                ? $"{backend}  |  Total {total:F2}s"
                : backend;
            header.Add(CommonUITK.CreateLabel(timing, "brain-model-tester-model-meta"));
            return header;
        }

        private VisualElement CreateResultCell(TestModel model, TestImage image)
        {
            bool enabled = _modelEnabled[model.Name] && _imageEnabled[image.path];
            VisualElement content = CommonUITK.CreateContainer("brain-model-tester-cell-content");
            if (!enabled)
            {
                content.AddToClassList("brain-model-tester-cell-disabled");
                content.Add(CommonUITK.CreateLabel("Not included"));
                return content;
            }

            (string model, string image) key = (model.Name, image.path);
            _results.TryGetValue(key, out string caption);
            Label captionLabel = CommonUITK.CreateLabel(
                string.IsNullOrEmpty(caption) ? "Not run yet" : caption,
                "brain-model-tester-caption");
            captionLabel.tooltip = caption ?? string.Empty;
            content.Add(captionLabel);

            VisualElement actions = CommonUITK.CreateContainer("brain-model-tester-cell-actions");
            Button run = CommonUITK.CreateIconButton(
                "Run this model and image",
                "d_PlayButton",
                () => _ = RunSingleFromUIAsync(model, image),
                "brain-model-tester-cell-run",
                null,
                null);
            run.SetEnabled(!_isRunning);
            actions.Add(run);
            string time = _cellTimes.TryGetValue(key, out float cellTime) ? $"{cellTime:F2}s" : string.Empty;
            actions.Add(CommonUITK.CreateLabel(time, "brain-model-tester-cell-time"));
            content.Add(actions);
            return content;
        }

        private void BuildPrompt()
        {
            _promptHost.Clear();
            Foldout foldout = CommonUITK.CreateFoldout(
                "Prompt",
                _showPrompt,
                value => _showPrompt = value,
                "Compare the default caption prompt with an optional tool-specific prompt.",
                "brain-model-tester-prompt");
            _promptHost.Add(foldout);

            VisualElement body = CommonUITK.CreateContainer("brain-model-tester-prompt-body");
            body.Add(CreatePromptColumn("Default", Intelligence.DefaultPrompt, true, null));

            if (_customPrompt == null)
            {
                VisualElement custom = CommonUITK.CreateContainer("brain-model-tester-prompt-column");
                VisualElement heading = CommonUITK.CreateContainer("brain-model-tester-prompt-heading");
                heading.Add(CommonUITK.CreateLabel("Custom", "brain-model-tester-prompt-label"));
                heading.Add(CommonUITK.CreateButton("Customize", () =>
                {
                    _customPrompt = Intelligence.DefaultPrompt;
                    _onCustomPromptChanged?.Invoke(_customPrompt);
                    BuildPrompt();
                }));
                custom.Add(heading);
                custom.Add(CommonUITK.CreateLabel("Using the default prompt.", "brain-model-tester-subtitle"));
                custom.AddToClassList("brain-model-tester-prompt-custom");
                body.Add(custom);
            }
            else
            {
                VisualElement custom = CreatePromptColumn("Custom", _customPrompt, false, () =>
                {
                    _customPrompt = null;
                    _onCustomPromptChanged?.Invoke(null);
                    BuildPrompt();
                });
                custom.AddToClassList("brain-model-tester-prompt-custom");
                body.Add(custom);
            }
            foldout.Add(body);
        }

        private VisualElement CreatePromptColumn(string title, string value, bool readOnly, Action useDefault)
        {
            VisualElement column = CommonUITK.CreateContainer("brain-model-tester-prompt-column");
            VisualElement heading = CommonUITK.CreateContainer("brain-model-tester-prompt-heading");
            heading.Add(CommonUITK.CreateLabel(title, "brain-model-tester-prompt-label"));
            if (useDefault != null) heading.Add(CommonUITK.CreateButton("Use Default", useDefault));
            column.Add(heading);

            if (readOnly)
            {
                ScrollView readOnlyText = new ScrollView(ScrollViewMode.Vertical);
                readOnlyText.AddToClassList("brain-model-tester-prompt-readonly");
                readOnlyText.Add(CommonUITK.CreateLabel(value ?? string.Empty));
                column.Add(readOnlyText);
                return column;
            }

            TextField field = new TextField
            {
                multiline = true,
                value = value ?? string.Empty
            };
            field.AddToClassList("brain-model-tester-prompt-field");
            field.RegisterValueChangedCallback(evt =>
            {
                _customPrompt = evt.newValue;
                _onCustomPromptChanged?.Invoke(_customPrompt);
            });
            column.Add(field);
            return column;
        }

        private void BuildFooter()
        {
            VisualElement footer = CommonUITK.CreateWindowFooter(0f, 0f, "brain-model-tester-footer");
            _statusLabel = CommonUITK.CreateLabel(string.Empty, "brain-model-tester-status");
            footer.Add(_statusLabel);
            _progress = new ProgressBar();
            _progress.AddToClassList("brain-model-tester-progress");
            footer.Add(_progress);
            _cancelButton = CommonUITK.CreateButton("Cancel", () => _cts?.Cancel(), "brain-model-tester-action");
            footer.Add(_cancelButton);
            _runAllButton = CommonUITK.CreateButton(
                "Run Selected Tests",
                () => _ = RunCaptionTestsFromUIAsync(),
                "brain-model-tester-action",
                "brain-model-tester-primary");
            footer.Add(_runAllButton);
            _root.Add(footer);
        }

        private async Task RunCaptionTestsFromUIAsync()
        {
            if (_isRunning) return;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            List<TestModel> models = _models.Where(model => _modelEnabled[model.Name]).ToList();
            List<TestImage> images = _testImages.Where(image => _imageEnabled[image.path]).ToList();
            _totalTests = models.Count * images.Count;
            _completedTests = 0;
            _isRunning = true;
            UpdateControls();

            try
            {
                foreach (TestModel model in models)
                {
                    if (images.Count == 0) continue;
                    token.ThrowIfCancellationRequested();
                    await RunCaptionCoreAsync(model, images[0], token, false);

                    float modelStartTime = Time.realtimeSinceStartup;
                    foreach (TestImage image in images)
                    {
                        token.ThrowIfCancellationRequested();
                        await RunCaptionCoreAsync(model, image, token, true);
                    }
                    _modelTotalTimes[model.Name] = Time.realtimeSinceStartup - modelStartTime;
                    ScheduleVisualUpdate(() => _matrix?.RefreshRow(model));
                }
            }
            catch (OperationCanceledException)
            {
                _activeStatus = "Cancelled";
            }
            finally
            {
                EndRun();
            }
        }

        private async Task RunSingleFromUIAsync(TestModel model, TestImage image)
        {
            if (_isRunning) return;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _totalTests = 1;
            _completedTests = 0;
            _isRunning = true;
            UpdateControls();
            try
            {
                await RunCaptionCoreAsync(model, image, token, true);
            }
            catch (OperationCanceledException)
            {
                _activeStatus = "Cancelled";
            }
            finally
            {
                EndRun();
            }
        }

        private async Task RunCaptionCoreAsync(TestModel model, TestImage image, CancellationToken token, bool countProgress)
        {
            int oldBackend = Intelligence.Settings.AIBackend;
            (string model, string image) key = (model.Name, image.path);
            try
            {
                token.ThrowIfCancellationRequested();
                _results[key] = "Running...";
                _activeStatus = $"Running {model.Name} on {Path.GetFileName(image.path)}";
                ScheduleVisualUpdate(() =>
                {
                    _matrix?.RefreshCell(model, image);
                    UpdateControls();
                });

                if (Intelligence.Settings is BrainSettings mutableSettings) mutableSettings.aiBackend = model.Backend;
                string prompt = (_customPrompt ?? Intelligence.DefaultPrompt)
                    .Replace("$filename", Path.GetFileName(image.path))
                    .Replace("$path", image.path);

                float startTime = Time.realtimeSinceStartup;
                List<CaptionResult> results = await CaptionEngine.CaptionImages(
                    new List<string> {image.path},
                    new List<string> {prompt},
                    model.Name,
                    null,
                    token);
                token.ThrowIfCancellationRequested();

                _results[key] = results?.FirstOrDefault()?.caption ?? string.Empty;
                _cellTimes[key] = Time.realtimeSinceStartup - startTime;
                _modelTotalTimes[model.Name] = _cellTimes
                    .Where(pair => pair.Key.model == model.Name)
                    .Sum(pair => pair.Value);
            }
            catch (OperationCanceledException)
            {
                _results[key] = "Cancelled";
                throw;
            }
            catch (Exception ex)
            {
                _results[key] = "Failed";
                Debug.LogError($"Error running single caption test: {ex.Message}");
            }
            finally
            {
                if (Intelligence.Settings is BrainSettings settings) settings.aiBackend = oldBackend;
                if (countProgress && !token.IsCancellationRequested) _completedTests++;
                ScheduleVisualUpdate(() =>
                {
                    _matrix?.RefreshCell(model, image);
                    _matrix?.RefreshRow(model);
                    UpdateControls();
                });
            }
        }

        private void EndRun()
        {
            bool cancelled = _cts?.IsCancellationRequested == true;
            _isRunning = false;
            _cts?.Dispose();
            _cts = null;
            if (!cancelled && _completedTests >= _totalTests && _totalTests > 0) _activeStatus = "Selected tests complete";
            ScheduleVisualUpdate(() =>
            {
                _matrix?.Rebuild();
                UpdateControls();
            });
        }

        private void UpdateControls()
        {
            if (_statusLabel == null) return;
            int selectedModels = _models?.Count(model => _modelEnabled[model.Name]) ?? 0;
            int selectedImages = _testImages?.Count(image => _imageEnabled[image.path]) ?? 0;
            _statusLabel.text = _isRunning
                ? (_activeStatus ?? "Running selected tests")
                : (_activeStatus ?? $"{selectedModels} model{(selectedModels == 1 ? string.Empty : "s")} and {selectedImages} image{(selectedImages == 1 ? string.Empty : "s")} selected");

            if (_progress != null)
            {
                _progress.style.display = _isRunning ? DisplayStyle.Flex : DisplayStyle.None;
                _progress.lowValue = 0f;
                _progress.highValue = Mathf.Max(1, _totalTests);
                _progress.value = _completedTests;
                _progress.title = _totalTests > 0 ? $"{_completedTests}/{_totalTests}" : string.Empty;
            }
            _cancelButton?.SetEnabled(_isRunning);
            if (_cancelButton != null) _cancelButton.style.display = _isRunning ? DisplayStyle.Flex : DisplayStyle.None;
            _runAllButton?.SetEnabled(!_isRunning && selectedModels > 0 && selectedImages > 0);
        }

        private void ScheduleVisualUpdate(Action action)
        {
            if (_root == null || _root.panel == null) return;
            _root.schedule.Execute(() =>
            {
                if (_root != null && _root.panel != null) action?.Invoke();
            });
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyCompactState(evt.newRect.width);
        }

        private void ApplyCompactState(float width)
        {
            bool compact = width > 0f && width < 760f;
            _root?.EnableInClassList(CompactClass, compact);
        }

        private static string GetBackendName(int backend)
        {
            switch (backend)
            {
                case 0:
                    return "BLIP";
                case 1:
                    return "Ollama";
                case 2:
                    return "LM Studio";
                default:
                    return "Unknown backend";
            }
        }

        private List<TestImage> GetTestImages()
        {
            List<TestImage> images = new List<TestImage>();
            if (string.IsNullOrEmpty(_imagePath) || !Directory.Exists(_imagePath)) return images;

            string[] files = Directory.GetFiles(_imagePath, "*.png")
                .OrderBy(path => path, StringComparer.InvariantCultureIgnoreCase)
                .ToArray();
            foreach (string file in files)
            {
                string assetPath = file.Replace("\\", "/");
                if (assetPath.StartsWith(Application.dataPath, StringComparison.Ordinal))
                {
                    assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                }
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                images.Add(new TestImage {path = file, texture = texture});
            }
            return images;
        }
    }

    [Serializable]
    internal sealed class TestImage
    {
        public string path;
        public Texture2D texture;
    }

    [Serializable]
    internal sealed class TestModel
    {
        public string Name;
        public int Backend;
    }
}
