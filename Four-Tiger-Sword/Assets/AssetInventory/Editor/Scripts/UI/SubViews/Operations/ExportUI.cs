using Automator;
using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed partial class ExportUI : BasicEditorUI
    {
        private const string TEMP_FOLDER = "AITemplateCache";
        private const int ICON_SIZE = 48;
        private const string ExportRootClass = "ai-export-root";
        private const string ExportTitleClass = "ai-export-title";
        private const string ExportSubtitleClass = "ai-export-subtitle";
        private const string ExportScrollClass = "ai-export-scroll";
        private const string ExportTileGridClass = "ai-export-tile-grid";
        private const string ExportTileClass = "ai-export-tile";
        private const string ExportTileIconClass = "ai-export-tile-icon";
        private const string ExportTileTitleClass = "ai-export-tile-title";
        private const string ExportTileDescriptionClass = "ai-export-tile-description";
        private static readonly List<string> ExportFileSelectionOptions = new List<string>
        {
            "All File Types",
            "Custom Selection"
        };

        private string _separator = ";";
        private bool _fileMode;
        private List<AssetInfo> _assets;
        private List<ED> _exportFields;
        private List<ED> _overrideFields;
        private List<ED> _exportTypes;
        private int _selectedExportOption;
        private bool _addHeader = true;
        private bool _showFields = true;
        private bool _clearTarget;
        private bool _overrideExisting;
        private List<AssetInfo> _packages;
        private int _packageCount;
        private bool _exportInProgress;
        private int _curProgress;
        private int _maxProgress;
        private ActionProgress _progress;
        private bool _autoDownload;
        private bool _flattenStructure;
        private bool _metaFiles;
        private ExportFileSelectionMode _exportFileSelectionMode = ExportFileSelectionMode.AllFileTypes;
        private bool _includeOtherExportTypes;

        // Wizard related fields
        private bool _wizardActive = true;
        private List<ExportTypeInfo> _exportTypeInfos;

        private FileSystemWatcher _watcher;
        private bool _triggerExport;
        private int _selectedTemplate;
        private string _templateFolder;
        private List<TemplateInfo> _templates;
        private string[] _templateNames;
        private string _overridesFolder;
        private List<string> _overrideCandidates;
        private bool _uitkActive;

        public void OnEnable()
        {
            LoadTemplates();
            PrepareOverrides();

            EditorApplication.update += () =>
            {
                if (_triggerExport) ExportTemplate();
            };

            // Initialize export type infos with descriptions and icons
            InitExportTypeInfos();
        }

        public void OnDisable()
        {
            if (_watcher != null) StopTemplateWatcher();
        }

        private void LoadTemplates()
        {
            _templates = TemplateUtils.LoadTemplates();
            _templateFolder = TemplateUtils.GetTemplateRootFolder();
            _templateNames = _templates.Select(t => t.name).ToArray();
        }

        public static ExportUI ShowWindow()
        {
            ExportUI window = GetWindow<ExportUI>("Asset Export");
            window.minSize = new Vector2(500, 320);

            return window;
        }

        public void Init(List<AssetInfo> assets, bool fileMode = false, int exportType = 0, int[] columns = null)
        {
            _fileMode = fileMode;
            _assets = assets;
            if (!_fileMode) _assets = _assets.Where(a => a.SafeName != Asset.NONE).ToList();

            _packages = assets.GroupBy(a => a.AssetId).Select(a => a.First()).ToList(); // cast to list to make it serializable during script reloads
            _packageCount = _packages.Count;
            _wizardActive = !_fileMode; // only one type supported right now
            if (_fileMode) _flattenStructure = true;

            _selectedExportOption = exportType;
            _exportFields = new List<ED>
            {
                new ED("Asset/Id"),
                new ED("Asset/ParentId"),
                new ED("Asset/ForeignId", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.ForeignId)),
                new ED("Asset/AssetRating", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Rating)),
                new ED("Asset/AssetSource", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Source)),
                new ED("Asset/AssetLink", false),
                new ED("Asset/Backup", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Backup)),
                new ED("Asset/BIRPCompatible", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.BIRP)),
                new ED("Asset/CompatibilityInfo", false),
                new ED("Asset/CurrentState", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.InternalState)),
                new ED("Asset/CurrentSubState", false),
                new ED("Asset/Description", false),
                new ED("Asset/DisplayCategory", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Category)),
                new ED("Asset/DisplayName", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Name)),
                new ED("Asset/DisplayPublisher", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Publisher)),
                new ED("Asset/ETag", false),
                new ED("Asset/Exclude", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Exclude)),
                new ED("Asset/Extract", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Extract)),
                new ED("Asset/FirstRelease", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.ReleaseDate)),
                new ED("Asset/HDRPCompatible", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.HDRP)),
                new ED("Asset/Hotness", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Popularity)),
                new ED("Asset/IsHidden", false),
                new ED("Asset/KeyFeatures", false),
                new ED("Asset/Keywords"),
                new ED("Asset/LastOnlineRefresh", false),
                new ED("Asset/LastRelease", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.UpdateDate)),
                new ED("Asset/LatestVersion", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Version)),
                new ED("Asset/License", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.License)),
                new ED("Asset/LicenseLocation", false),
                new ED("Asset/Location", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Location)),
                new ED("Asset/NoIndex", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.NoIndex)),
                new ED("Asset/OriginalLocation", false),
                new ED("Asset/PackageSize", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Size)),
                new ED("Asset/PackageSource"),
                new ED("Asset/PackageTags", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Tags)),
                new ED("Asset/PriceEur", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Price)),
                new ED("Asset/PriceUsd", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Price)),
                new ED("Asset/PriceCny", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Price)),
                new ED("Asset/PurchaseDate", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.PurchaseDate)),
                new ED("Asset/RatingCount", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.RatingCount)),
                new ED("Asset/Registry", false),
                new ED("Asset/ReleaseNotes", false),
                new ED("Asset/Repository", false),
                new ED("Asset/Revision"),
                new ED("Asset/SafeCategory"),
                new ED("Asset/SafeName"),
                new ED("Asset/SafePublisher"),
                new ED("Asset/Slug", false),
                new ED("Asset/SupportedUnityVersions", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.UnityVersions)),
                new ED("Asset/UpdateStrategy", false),
                new ED("Asset/URPCompatible", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.URP)),
                new ED("Asset/UseAI", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.AICaptions)),
                new ED("Asset/UseCodeIndex", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.CodeIndex)),
                new ED("Asset/UseSemanticIndex", false, IsVisibleColumn(columns, AssetTreeViewControl.Columns.SemanticIndex)),
                new ED("Asset/Version", true, IsVisibleColumn(columns, AssetTreeViewControl.Columns.Version))
            };
            LoadCSVSettings();

            _overrideFields = new List<ED>
            {
                new ED("Asset/AssetRating", false),
                new ED("Asset/BIRPCompatible", false),
                new ED("Asset/CompatibilityInfo", false),
                new ED("Asset/Description", false),
                new ED("Asset/DisplayCategory", false),
                new ED("Asset/DisplayName", false),
                new ED("Asset/DisplayPublisher", false),
                new ED("Asset/FirstRelease", false),
                new ED("Asset/ForeignId", false),
                new ED("Asset/HDRPCompatible", false),
                new ED("Asset/Hotness", false),
                new ED("Asset/KeyFeatures", false),
                new ED("Asset/Keywords", false),
                new ED("Asset/LastRelease", false),
                new ED("Asset/LatestVersion", false),
                new ED("Asset/License", false),
                new ED("Asset/LicenseLocation", false),
                new ED("Asset/PackageTags", false),
                new ED("Asset/PriceEur", false),
                new ED("Asset/PriceUsd", false),
                new ED("Asset/PriceCny", false),
                new ED("Asset/PurchaseDate", false),
                new ED("Asset/RatingCount", false),
                new ED("Asset/Registry", false),
                new ED("Asset/ReleaseNotes", false),
                new ED("Asset/Repository", false),
                new ED("Asset/Revision", false),
                new ED("Asset/SafeCategory", false),
                new ED("Asset/SafePublisher", false),
                new ED("Asset/Slug", false),
                new ED("Asset/SupportedUnityVersions", false),
                new ED("Asset/URPCompatible", false),
                new ED("Asset/Version", false)
            };
            _exportTypes = new List<ED>();
            IReadOnlyList<AI.AssetGroup> exportGroups = ExportFileTypeFilter.GetAvailableGroups();
            foreach (AI.AssetGroup group in exportGroups)
            {
                _exportTypes.Add(new ED(group.ToString(), IsTypicalExportGroup(group)));
            }

            // Initialize export type infos with descriptions and icons
            InitExportTypeInfos();
            BuildIfReady();
        }

        private static bool IsTypicalExportGroup(AI.AssetGroup group)
        {
            return group == AI.AssetGroup.Audio
                || group == AI.AssetGroup.Images
                || group == AI.AssetGroup.Videos
                || group == AI.AssetGroup.Models;
        }

        private void InitExportTypeInfos()
        {
            // Define export type information with descriptions and compact icons
            _exportTypeInfos = new List<ExportTypeInfo>
            {
                new ExportTypeInfo(
                    0,
                    "CSV Export",
                    "Export metadata to CSV for reports and spreadsheets.",
                    CreateCompactIcon("CSV Export", new Color(0.4f, 0.6f, 0.9f))),

                new ExportTypeInfo(
                    4,
                    "Template Export",
                    "Generate documentation or catalogs using templates.",
                    CreateCompactIcon("Template Export", new Color(0.9f, 0.4f, 0.6f))),

                new ExportTypeInfo(
                    2,
                    "Asset Export",
                    "Export actual asset files to an external folder.",
                    CreateCompactIcon("Asset Export", new Color(0.4f, 0.8f, 0.5f))),

                new ExportTypeInfo(
                    1,
                    "License Export",
                    "Generate Markdown with license info for all packages.",
                    CreateCompactIcon("License Export", new Color(0.7f, 0.5f, 0.9f))),

                new ExportTypeInfo(
                    3,
                    "Package Override",
                    "Create JSON files to customize package metadata.",
                    CreateCompactIcon("Package Override", new Color(0.9f, 0.7f, 0.4f)))
            };
        }

        /// <summary>
        /// Creates a compact 48x48 icon for an export type
        /// </summary>
        private Texture2D CreateCompactIcon(string title, Color accentColor)
        {
            int size = ICON_SIZE;
            Texture2D texture = new Texture2D(size, size);
            texture.hideFlags = HideFlags.HideAndDontSave;

            // Theme-aware colors
            bool isDark = EditorGUIUtility.isProSkin;
            Color bgColor = isDark ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.85f, 0.85f, 0.85f);
            Color fgColor = isDark ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.3f, 0.3f, 0.3f);
            Color accent = Color.Lerp(accentColor, fgColor, 0.3f);

            // Fill background
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bgColor;
            texture.SetPixels(pixels);

            // Draw icon based on export type
            if (title.Contains("CSV"))
            {
                // Grid/table icon (3x3 cells)
                DrawFilledRect(texture, 8, 8, 32, 32, accent);
                // Grid lines
                for (int x = 8; x <= 40; x++) { texture.SetPixel(x, 18, bgColor); texture.SetPixel(x, 28, bgColor); }
                for (int y = 8; y <= 40; y++) { texture.SetPixel(18, y, bgColor); texture.SetPixel(28, y, bgColor); }
            }
            else if (title.Contains("License"))
            {
                // Document with checkmark
                DrawFilledRect(texture, 12, 6, 24, 36, accent);
                // Checkmark
                DrawLine(texture, 18, 20, 22, 16, fgColor, 2);
                DrawLine(texture, 22, 16, 30, 28, fgColor, 2);
            }
            else if (title.Contains("Asset"))
            {
                // Folder icon
                DrawFilledRect(texture, 6, 10, 36, 26, accent);
                DrawFilledRect(texture, 6, 32, 14, 6, accent); // Tab
            }
            else if (title.Contains("Override"))
            {
                // JSON curly braces
                DrawCurlyBrace(texture, 14, 8, 32, true, accent);
                DrawCurlyBrace(texture, 34, 8, 32, false, accent);
            }
            else if (title.Contains("Template"))
            {
                // Document with code brackets <>
                DrawFilledRect(texture, 10, 6, 28, 36, accent);
                // < bracket
                DrawLine(texture, 20, 18, 16, 24, fgColor, 2);
                DrawLine(texture, 16, 24, 20, 30, fgColor, 2);
                // > bracket
                DrawLine(texture, 28, 18, 32, 24, fgColor, 2);
                DrawLine(texture, 32, 24, 28, 30, fgColor, 2);
            }

            texture.Apply();
            return texture;
        }

        private void DrawFilledRect(Texture2D tex, int x, int y, int w, int h, Color color)
        {
            for (int py = y; py < y + h && py < tex.height; py++)
                for (int px = x; px < x + w && px < tex.width; px++)
                    tex.SetPixel(px, py, color);
        }

        private void DrawLine(Texture2D tex, int x1, int y1, int x2, int y2, Color color, int thickness = 1)
        {
            int dx = Mathf.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
            int dy = -Mathf.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                for (int t = -thickness / 2; t <= thickness / 2; t++)
                {
                    if (x1 + t >= 0 && x1 + t < tex.width) tex.SetPixel(x1 + t, y1, color);
                    if (y1 + t >= 0 && y1 + t < tex.height) tex.SetPixel(x1, y1 + t, color);
                }
                if (x1 == x2 && y1 == y2) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x1 += sx; }
                if (e2 <= dx) { err += dx; y1 += sy; }
            }
        }

        private void DrawCurlyBrace(Texture2D tex, int x, int y, int height, bool openBrace, Color color)
        {
            int dir = openBrace ? -1 : 1;
            int midY = y + height / 2;
            // Top curve
            for (int i = 0; i < 6; i++) tex.SetPixel(x + dir * (3 - Mathf.Abs(i - 3)), y + i, color);
            // Top vertical
            for (int i = 6; i < height / 2 - 3; i++) tex.SetPixel(x, y + i, color);
            // Middle point
            for (int i = -3; i <= 3; i++) tex.SetPixel(x + dir * (3 - Mathf.Abs(i)), midY + i, color);
            // Bottom vertical
            for (int i = height / 2 + 3; i < height - 6; i++) tex.SetPixel(x, y + i, color);
            // Bottom curve
            for (int i = 0; i < 6; i++) tex.SetPixel(x + dir * (3 - Mathf.Abs(i - 3)), y + height - 6 + i, color);
        }

        private bool IsVisibleColumn(int[] columns, AssetTreeViewControl.Columns column)
        {
            return columns != null && columns.Contains((int)column);
        }

        private void CreateGUI()
        {
            _uitkActive = true;
            BuildContent();
        }

        private void BuildIfReady()
        {
            if (_uitkActive && rootVisualElement != null && rootVisualElement.childCount > 0)
            {
                BuildContent();
            }
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);
            root.AddToClassList(ExportRootClass);

            if (_assets == null || _assets.Count == 0)
            {
                Button openInventory = AssetInventoryUITK.CreatePrimaryButton("Open Asset Inventory", () =>
                {
                    MenuIntegration.ShowWindow();
                    GetWindow<IndexUI>("Asset Inventory").Focus();
                    Close();
                });
                root.Add(AssetInventoryUITK.CreateEmptyState(
                    "Nothing selected for export",
                    "Select packages or files in Asset Inventory, then open Export again.",
                    openInventory));
                return;
            }

            if (_wizardActive)
            {
                BuildExportTypeChooser(root);
            }
            else
            {
                BuildLegacyOptions(root);
            }
        }

        private void BuildExportTypeChooser(VisualElement root)
        {
            Label title = AssetInventoryUITK.CreateCopyLabel("Select Export Type");
            title.AddToClassList(ExportTitleClass);
            root.Add(title);

            Label subtitle = AssetInventoryUITK.CreateCopyLabel(GetExportSelectionSummary());
            subtitle.AddToClassList(ExportSubtitleClass);
            root.Add(subtitle);

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList(ExportScrollClass);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            VisualElement grid = new VisualElement();
            grid.AddToClassList(ExportTileGridClass);
            scroll.Add(grid);

            foreach (ExportTypeInfo info in _exportTypeInfos)
            {
                grid.Add(CreateExportTypeTile(info));
            }

            root.Add(scroll);
        }

        private Button CreateExportTypeTile(ExportTypeInfo info)
        {
            Button tile = new Button(() =>
            {
                _selectedExportOption = info.Index;
                _wizardActive = false;
                BuildContent();
            });
            tile.text = string.Empty;
            tile.tooltip = info.Description;
            tile.AddToClassList(ExportTileClass);

            Image icon = new Image
            {
                image = info.Icon,
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList(ExportTileIconClass);
            tile.Add(icon);

            Label title = AssetInventoryUITK.CreateCopyLabel(info.Name);
            title.AddToClassList(ExportTileTitleClass);
            tile.Add(title);

            Label description = AssetInventoryUITK.CreateCopyLabel(info.Description);
            description.AddToClassList(ExportTileDescriptionClass);
            tile.Add(description);

            return tile;
        }

        private string GetExportSelectionSummary()
        {
            if (_fileMode) return $"{_assets.Count:N0} files selected";
            return _packageCount == 1 ? "1 package selected" : $"{_packageCount:N0} packages selected";
        }

        private void BuildLegacyOptions(VisualElement root)
        {
            BuildNativeOptions(root);
        }

        private async void PrepareOverrides()
        {
            if (_templates == null || _templates.Count == 0)
            {
                _overrideCandidates = new List<string>();
                return;
            }

            _selectedTemplate = Mathf.Clamp(_selectedTemplate, 0, _templates.Count - 1);
            _overridesFolder = IOUtils.CreateTempFolder(TEMP_FOLDER, true);
            await TemplateExport.ResolveInheritance(_templates[_selectedTemplate], _overridesFolder, _templates);
            _overrideCandidates = IOUtils.GetFiles(_overridesFolder, "", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.InvariantCultureIgnoreCase).ToList();
        }

        private void ShowOverrides()
        {
            GenericMenu menu = new GenericMenu();
            foreach (string file in _overrideCandidates)
            {
                string relPath = file.Substring(_overridesFolder.Length + 1);
                string target = Path.Combine(AI.Config.templateExportSettings.devFolder, relPath);
                if (File.Exists(target))
                {
                    menu.AddDisabledItem(new GUIContent(relPath));
                }
                else
                {
                    menu.AddItem(new GUIContent(relPath), false, () => OverrideFile(file));
                }
            }
            menu.ShowAsContext();
        }

        private void OverrideFile(string file)
        {
            string target = Path.Combine(AI.Config.templateExportSettings.devFolder, file.Substring(_overridesFolder.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, true);
        }

        private void CreateTemplate(string newName)
        {
            string saveFolder = TemplateUtils.GetTemplateSaveFolder();
            string destination = Path.Combine(saveFolder, $"{newName}.zip.bytes");

            if (File.Exists(destination))
            {
                EditorUtility.DisplayDialog("Error", "A template with that name already exists.", "OK");
                return;
            }

            // create zip
            CompressionUtil.CreateEmptyZip(destination);

            AssetDatabase.Refresh();
            LoadTemplates();
        }

        private void CopyTemplate(string newName)
        {
            string saveFolder = TemplateUtils.GetTemplateSaveFolder();
            string source = _templates[_selectedTemplate].path;
            string safeName = AssetUtils.GuessSafeName(newName).Replace(" ", "");
            string destination = Path.Combine(saveFolder, $"{safeName}.zip.bytes");
            if (File.Exists(destination))
            {
                EditorUtility.DisplayDialog("Error", "A template with that name already exists.", "OK");
                return;
            }
            File.Copy(source, destination);
            if (_templates[_selectedTemplate].hasDescriptor)
            {
                string descriptor = _templates[_selectedTemplate].GetDescriptorPath();
                string newDescriptor = Path.Combine(saveFolder, $"{safeName}.json");
                File.Copy(descriptor, newDescriptor);

                // adjust descriptor
                TemplateInfo ti = JsonConvert.DeserializeObject<TemplateInfo>(File.ReadAllText(newDescriptor));
                ti.name = newName;
                ti.date = DateTime.Now;
                ti.version = 1;
                ti.readOnly = false;
                File.WriteAllText(newDescriptor, JsonConvert.SerializeObject(ti, Formatting.Indented));
            }

            AssetDatabase.Refresh();
            LoadTemplates();
        }

        private void ExtendTemplate(string newName)
        {
            string saveFolder = TemplateUtils.GetTemplateSaveFolder();
            string source = _templates[_selectedTemplate].path;
            string safeName = AssetUtils.GuessSafeName(newName).Replace(" ", "");
            string destination = Path.Combine(saveFolder, $"{safeName}.zip.bytes");
            string newDescriptor = Path.Combine(saveFolder, $"{safeName}.json");

            if (File.Exists(destination))
            {
                EditorUtility.DisplayDialog("Error", "A template with that name already exists.", "OK");
                return;
            }

            // create descriptor and copy from original
            TemplateInfo ti = new TemplateInfo();
            ti.name = newName;
            ti.inheritFrom = _templates[_selectedTemplate].GetNameFromFile();
            ti.needsDataPath = _templates[_selectedTemplate].needsDataPath;
            ti.needsImagePath = _templates[_selectedTemplate].needsImagePath;
            ti.fixedTargetFolder = _templates[_selectedTemplate].fixedTargetFolder;
            ti.entryPath = _templates[_selectedTemplate].entryPath;
            ti.parameters = _templates[_selectedTemplate].parameters;
            ti.readOnly = false;
            ti.isSample = false;
            ti.date = DateTime.Now;
            File.WriteAllText(newDescriptor, JsonConvert.SerializeObject(ti, Formatting.Indented));

            // create zip
            CompressionUtil.CreateEmptyZip(destination);

            AssetDatabase.Refresh();
            LoadTemplates();
        }

        private void PackageDevTemplate()
        {
            TemplateInfo ti = _templates[_selectedTemplate];
            string source = AI.Config.templateExportSettings.devFolder;
            string target = ti.path;

            CompressionUtil.CompressFolder(source, target);

            if (ti.hasDescriptor)
            {
                ti.date = DateTime.Now;
                File.WriteAllText(ti.GetDescriptorPath(), JsonConvert.SerializeObject(ti, Formatting.Indented));
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Template Export", $"Template '{ti.GetNameFromFile()}' has been exported to '{target}'.", "OK");
        }

        private void StopTemplateWatcher()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        private void StartTemplateWatcher(string path)
        {
            _watcher = new FileSystemWatcher();
            _watcher.Path = path;
            _watcher.IncludeSubdirectories = true;
            _watcher.Filter = "*.*";
            _watcher.InternalBufferSize = 65536;

            _watcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite;

            _watcher.Changed += OnChanged;
            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += (_, args) => { Debug.LogWarning($"Template dev folder monitoring error: {args.GetException()}"); };

            _watcher.EnableRaisingEvents = true;
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            Debug.Log($"Picking up template file rename: {e.OldFullPath} -> {e.FullPath}");
            _triggerExport = true;
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            Debug.Log($"Picking up template file delete: {e.FullPath}");
            _triggerExport = true;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            Debug.Log($"Picking up template file create: {e.FullPath}");
            _triggerExport = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            Debug.Log($"Picking up template file change: {e.FullPath}");
            _triggerExport = true;
        }

        private async void ExportTemplate()
        {
            if (_exportInProgress) return;
            _triggerExport = false;
            _exportInProgress = true;

            AI.AskForAffiliate();

            Debug.Log("Export");
            try
            {
                TemplateExportEnvironment env = AI.Config.templateExportSettings.environments[AI.Config.templateExportSettings.environmentIndex];

                // reload template info from disk to support easy template changes
                if (AI.Config.templateExportSettings.devMode) LoadTemplates();

                await AI.Actions.RunWithProgress<TemplateExport>(
                    ActionHandler.ACTION_SUB_PACKAGES_INDEX,
                    "Indexing sub-packages",
                    exp => exp.Run(
                        _assets,
                        _templates[_selectedTemplate],
                        _templates,
                        AI.Config.templateExportSettings,
                        env
                    ));

                if (AI.Config.templateExportSettings.revealResult) EditorUtility.RevealInFinder(env.publishFolder);
            }
            catch (Exception e)
            {
                Debug.LogError($"Exporting template failed: {e}");
            }
            _exportInProgress = false;
        }

        private async void ExportAssets()
        {
            string folder = EditorUtility.OpenFolderPanel("Select storage folder for exports", AI.Config.exportFolder2, "");
            if (string.IsNullOrEmpty(folder)) return;

            if (_clearTarget && Directory.Exists(folder)) await IOUtils.DeleteFileOrDirectory(folder);
            Directory.CreateDirectory(folder);

            AI.Config.exportFolder2 = Path.GetFullPath(folder);
            AI.SaveConfig();

            _exportInProgress = true;
            _curProgress = 0;
            _maxProgress = _packages.Count;
            ExportFileTypeFilter fileTypeFilter = CreateExportFileTypeFilter();

            foreach (AssetInfo info in _packages)
            {
                _curProgress++;
                await Task.Yield();

                if (!info.IsIndexed)
                {
                    Debug.LogError($"Skipping package '{info}' since it is not yet indexed.");
                    continue;
                }

                if (!info.IsDownloaded && !info.IsMaterialized)
                {
                    if (info.IsAbandoned)
                    {
                        Debug.LogWarning($"Package '{info}' is not locally available and also abandoned and cannot be downloaded anymore. Continuing with next package.");
                        continue;
                    }
                    if (!_autoDownload)
                    {
                        Debug.LogWarning($"Package '{info}' is not downloaded and cannot be exported. Continuing with next package.");
                        continue;
                    }
                    AI.GetObserver().Attach(info);
                    if (!info.PackageDownloader.IsDownloadSupported()) continue;

                    info.PackageDownloader.Download(true);
                    do
                    {
                        await Task.Yield();
                    } while (info.IsDownloading());
                    await Task.Delay(3000); // ensure all file operations have finished, can otherwise lead to issues
                    PackageDownloadCompletion.SyncPackage(info);
                    if (!info.IsDownloaded)
                    {
                        Debug.LogError($"Downloading '{info}' failed. Continuing with next package.");
                        continue;
                    }
                }

                string targetFolder = Path.Combine(folder, _flattenStructure ? "" : info.SafeName);
                Directory.CreateDirectory(targetFolder);

                // extract package
                string cachePath = Paths.GetMaterializedAssetPath(info.ToAsset());
                bool existing = Directory.Exists(cachePath);

                // gather all indexed files
                IEnumerable<AssetFile> files;
                if (_fileMode)
                {
                    // files to export are already known
                    files = _assets.Where(a => a.AssetId == info.AssetId);
                }
                else
                {
                    files = DBAdapter.DB.Query<AssetFile>("SELECT * FROM AssetFile WHERE AssetId = ?", info.AssetId).ToList();
                }
                foreach (AssetFile af in files)
                {
                    if (!_fileMode && !fileTypeFilter.Includes(af.Type)) continue;

                    string targetFile = Path.Combine(targetFolder, _flattenStructure ? af.FileName : af.GetPath(true));
                    string targetMeta = targetFile + ".meta";
                    if (File.Exists(targetFile) && (!_metaFiles || File.Exists(targetMeta))) continue;

                    string sourceFile = await Assets.EnsureMaterialized(info.ToAsset(), af);
                    if (string.IsNullOrEmpty(sourceFile)) continue;

                    string targetDir = Directory.GetParent(targetFile)?.ToString();
                    if (targetDir == null) continue;

                    Directory.CreateDirectory(targetDir);
                    File.Copy(sourceFile, targetFile, true);

                    if (_metaFiles)
                    {
                        string sourceMeta = sourceFile + ".meta";
                        if (File.Exists(sourceMeta)) File.Copy(sourceMeta, targetMeta, true);
                    }
                }
                if (!existing) await IOUtils.DeleteFileOrDirectory(cachePath);
            }
            _exportInProgress = false;
            EditorUtility.RevealInFinder(folder);
        }

        private ExportFileTypeFilter CreateExportFileTypeFilter()
        {
            if (_exportFileSelectionMode == ExportFileSelectionMode.AllFileTypes)
            {
                return ExportFileTypeFilter.CreateAll();
            }

            List<AI.AssetGroup> selectedGroups = new List<AI.AssetGroup>();
            foreach (ED type in _exportTypes)
            {
                if (type.isSelected && Enum.TryParse(type.pointer, out AI.AssetGroup group))
                {
                    selectedGroups.Add(group);
                }
            }
            return ExportFileTypeFilter.CreateCustom(selectedGroups, _includeOtherExportTypes);
        }

        private async void ExportOverrides()
        {
            _exportInProgress = true;
            _curProgress = 0;
            _maxProgress = _packages.Count;

            foreach (AssetInfo info in _packages)
            {
                _curProgress++;
                if (info.AssetSource != Asset.Source.CustomPackage && info.AssetSource != Asset.Source.Archive)
                {
                    Debug.LogWarning($"Skipping package '{info}' since it is not a custom package or archive.");
                    continue;
                }
                await Task.Yield();

                string targetFile = info.GetLocation(true) + ".overrides.json";
                if (!_overrideExisting && File.Exists(targetFile)) continue;

                PackageOverrides po = new PackageOverrides();
                foreach (ED field in _overrideFields.Where(f => f.isSelected))
                {
                    switch (field.field)
                    {
                        case "PackageTags":
                            po.tags = info.PackageTags.Select(pt => pt.Name).ToArray();
                            break;

                        default:
                            if (field.FieldInfo != null)
                            {
                                FieldInfo fi = typeof (PackageOverrides).GetField(field.field.ToLowercaseFirstLetter());
                                if (fi != null)
                                {
                                    fi.SetValue(po, field.FieldInfo.GetValue(info));
                                }
                                else
                                {
                                    Debug.LogError($"Override field '{field.field}' not found.");
                                }
                            }
                            else
                            {
                                Debug.LogError($"Override source field '{field.field}' not found.");
                            }
                            break;
                    }
                }

                File.WriteAllText(targetFile, JsonConvert.SerializeObject(po, new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                    DefaultValueHandling = DefaultValueHandling.Ignore
                }));
            }
            _exportInProgress = false;
        }

        private void ExportLicenses()
        {
            string file = EditorUtility.SaveFilePanel("Target file", AI.Config.exportFolder3, "ThirdParty", "md");
            if (string.IsNullOrEmpty(file)) return;

            _exportInProgress = true;

            AI.Config.exportFolder3 = Directory.GetParent(Path.GetFullPath(file))?.ToString();
            AI.SaveConfig();

            // TODO: switch to configurable templates
            List<string> result = new List<string>();
            result.Add("# Third Party Licenses");
            result.Add("");
            result.Add("The following third-party packages are included: ");
            result.Add("");

            List<AssetInfo> list = _assets.Where(a => !string.IsNullOrWhiteSpace(a.License))
                .GroupBy(a => a.GetDisplayName() + " - " + a.License)
                .Select(g => g.First())
                .OrderBy(a => a.GetDisplayName())
                .ToList();
            foreach (AssetInfo info in list)
            {
                result.Add($"## {info.GetDisplayName(true)}");
                result.Add("");
                result.Add(info.License);
                if (!string.IsNullOrWhiteSpace(info.LicenseLocation)) result.Add($"([Details]({info.LicenseLocation}))");
                result.Add("");
            }
            try
            {
                File.WriteAllLines(file, result);
                EditorUtility.RevealInFinder(file);
            }
            catch (Exception e)
            {
                Debug.LogError($"Exporting to file failed: {e}");
                EditorUtility.DisplayDialog("Export Failed", "License export failed. Most likely the target file is already opened in another application. See console for details.", "OK");
            }

            _exportInProgress = false;

        }

        private void ExportCSV()
        {
            SaveCSVSettings();

            string currentFile = AI.Config.csvExportSettings?.exportFile;
            string initialDirectory = !string.IsNullOrWhiteSpace(currentFile)
                ? Directory.GetParent(Path.GetFullPath(currentFile))?.ToString()
                : AI.Config.exportFolder;
            string initialName = !string.IsNullOrWhiteSpace(currentFile)
                ? Path.GetFileNameWithoutExtension(currentFile)
                : Path.GetFileNameWithoutExtension(CSVExport.DEFAULT_FILE_NAME);

            string file = EditorUtility.SaveFilePanel("Target file", initialDirectory, initialName, "csv");
            if (string.IsNullOrEmpty(file)) return;

            _exportInProgress = true;

            string fullPath = Path.GetFullPath(file);
            AI.Config.exportFolder = Directory.GetParent(fullPath)?.ToString();
            AI.Config.csvExportSettings.exportFile = fullPath;
            SaveCSVSettings();

            CSVExport exporter = new CSVExport();
            try
            {
                exporter.Run(_assets, AI.Config.csvExportSettings, fullPath);
                EditorUtility.RevealInFinder(fullPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"Exporting to file failed: {e}");
                EditorUtility.DisplayDialog("Export Failed", "CSV export failed. Most likely the target file is already opened in another application. See console for details.", "OK");
            }

            _exportInProgress = false;
        }

        private void OnInspectorUpdate()
        {
            Repaint();
            UpdateNativeExportProgress();
        }

        private void LoadCSVSettings()
        {
            if (AI.Config.csvExportSettings == null) AI.Config.csvExportSettings = new CSVExportSettings();
            AI.Config.csvExportSettings.EnsureDefaults();

            _separator = AI.Config.csvExportSettings.separator;
            _addHeader = AI.Config.csvExportSettings.addHeader;

            HashSet<string> selectedFields = new HashSet<string>(AI.Config.csvExportSettings.selectedFields);
            foreach (ED field in _exportFields)
            {
                field.isSelected = selectedFields.Contains(field.pointer);
            }
        }

        private void SaveCSVSettings()
        {
            if (AI.Config.csvExportSettings == null) AI.Config.csvExportSettings = new CSVExportSettings();

            AI.Config.csvExportSettings.separator = _separator;
            AI.Config.csvExportSettings.addHeader = _addHeader;
            AI.Config.csvExportSettings.selectedFields = _exportFields
                .Where(field => field.isSelected)
                .Select(field => field.pointer)
                .ToList();
            AI.Config.csvExportSettings.EnsureDefaults();
            AI.SaveConfig();
        }
    }

    /// <summary>
    /// Class to hold export type information for the wizard UI
    /// </summary>
    public class ExportTypeInfo
    {
        public int Index { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public Texture Icon { get; private set; }

        /// <summary>Initializes export type info from the supplied index, name, description, and icon.</summary>
        public ExportTypeInfo(int index, string name, string description, Texture icon)
        {
            Index = index;
            Name = name;
            Description = description;
            Icon = icon;
        }
    }

    [Serializable]
    public sealed class ED
    {
        public string pointer;
        public bool isDefault;
        public bool isVisibleColumn;
        public bool isSelected;

        public string table;
        public string field;

        public PropertyInfo FieldInfo
        {
            get
            {
                if (field == null) return null;
                if (_fieldInfo == null) _fieldInfo = typeof (AssetInfo).GetProperty(field);
                return _fieldInfo;
            }
        }

        private PropertyInfo _fieldInfo;

        public ED(string pointer, bool isDefault = true, bool isVisibleColumn = false)
        {
            this.isDefault = isDefault;
            this.isVisibleColumn = isVisibleColumn;
            this.pointer = pointer;

            isSelected = isDefault;

            if (pointer.IndexOf('/') >= 0)
            {
                table = pointer.Split('/')[0];
                field = pointer.Split('/')[1];
            }
        }
    }
}
