using System;
using System.Collections.Generic;
using System.IO;
using Database;
using ImpossibleRobert.Common;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
#if !USE_TUTORIALS
using UnityEditor.PackageManager;
#endif
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const string MainShellClass = "ai-main-shell";
        private const string MainToolbarClass = "ai-main-toolbar";
        private const string MainToolbarCompactClass = "ai-main-toolbar-compact";
        private const string MainToolbarLeftClass = "ai-main-toolbar-left";
        private const string MainTabsClass = "ai-main-tabs";
        private const string MainTabClass = "ai-main-tab";
        private const string MainTabActiveClass = "ai-main-tab-active";
        private const string MainToolbarStatusClass = "ai-main-toolbar-status";
        private const string MainToolbarIconClass = "ai-main-toolbar-icon";
        private const string MainCustomizationBannerClass = "ai-main-customization-banner";
        private const string MainCustomizationMessageClass = "ai-main-customization-message";
        private const string MainCustomizationActionsClass = "ai-main-customization-actions";
        private const string MainBodyClass = "ai-main-body";
        private const string MainNativeBodyClass = "ai-main-native-body";
        private const string MainBlockerBodyClass = "ai-main-blocker-body";
        private const string MainBlockerContentClass = "ai-main-blocker-content";
        private const string MainBlockerTitleClass = "ai-main-blocker-title";
        private const string MainBlockerActionsClass = "ai-main-blocker-actions";
        private const string MainBlockerErrorClass = "ai-main-blocker-error";
        private const string AboutPanelClass = "ai-about-panel";
        private const string AboutPanelTitleClass = "ai-about-panel-title";
        private const string AboutPanelCopyClass = "ai-about-panel-copy";
        private const string AboutButtonRowClass = "ai-about-button-row";

        private VisualElement _mainToolbar;
        private VisualElement _mainTabs;
        private VisualElement _mainToolbarStatus;
        private VisualElement _mainCustomizationBanner;
        private VisualElement _mainBody;
        private VisualElement _nativeBlockerBody;
        private VisualElement _nativeSetupBody;
        private VisualElement _nativeSearchBody;
        private VisualElement _nativePackagesBody;
        private VisualElement _nativeAboutBody;
        private VisualElement _nativeCodeBody;
        private VisualElement _nativeReportingBody;
        private VisualElement _nativeSettingsBody;
        private CommonProgressOverlay _nativeProgressOverlay;
        private readonly CommonScrollViewState _nativeScrollViewState = new CommonScrollViewState();
        private bool _nativeShellContentBlocked = true;
        private int _nativeBlockerStateHash = int.MinValue;
        private int _mainToolbarStateHash = int.MinValue;

        private enum NativeShellBlockerKind
        {
            None,
            Initializing,
            PlayMode,
            Configuration,
            Database,
            DatabaseVersion,
            Upgrade
        }

        private void CreateGUI()
        {
            BuildUITKShell();
        }

        private void BuildUITKShell()
        {
            _uitkShellActive = true;
            _searchPreviewSessionInitialized = false;

            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root, false);
            root.AddToClassList(MainShellClass);
            root.UnregisterCallback<KeyDownEvent>(OnNativeShellKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnNativeShellKeyDown, TrickleDown.TrickleDown);

            _mainToolbar = new VisualElement();
            _mainToolbar.AddToClassList(MainToolbarClass);
            root.Add(_mainToolbar);

            _mainCustomizationBanner = BuildUITKCustomizationBanner();
            root.Add(_mainCustomizationBanner);

            _mainBody = new VisualElement();
            _mainBody.AddToClassList(MainBodyClass);
            root.Add(_mainBody);

            _nativeBlockerBody = new VisualElement();
            _nativeBlockerBody.AddToClassList(MainBlockerBodyClass);
            _mainBody.Add(_nativeBlockerBody);

            _nativeSetupBody = new VisualElement();
            _nativeSetupBody.AddToClassList(MainNativeBodyClass);
            _mainBody.Add(_nativeSetupBody);

            _nativeSearchBody = new VisualElement();
            _nativeSearchBody.AddToClassList(MainNativeBodyClass);
            _mainBody.Add(_nativeSearchBody);

            _nativePackagesBody = new VisualElement();
            _nativePackagesBody.AddToClassList(MainNativeBodyClass);
            _mainBody.Add(_nativePackagesBody);

            _nativeAboutBody = new VisualElement();
            _nativeAboutBody.AddToClassList(MainNativeBodyClass);
            _mainBody.Add(_nativeAboutBody);

            _nativeCodeBody = new VisualElement();
            _nativeCodeBody.AddToClassList(MainNativeBodyClass);
            _mainBody.Add(_nativeCodeBody);

            _nativeReportingBody = new VisualElement();
            _nativeReportingBody.AddToClassList(MainNativeBodyClass);
            _mainBody.Add(_nativeReportingBody);

            _nativeSettingsBody = new VisualElement();
            _nativeSettingsBody.AddToClassList(MainNativeBodyClass);
            _mainBody.Add(_nativeSettingsBody);

            _nativeProgressOverlay = AssetInventoryUITK.CreateProgressOverlay();
            root.Add(_nativeProgressOverlay);

            RefreshUITKShell();
            root.schedule.Execute(RefreshUITKShell).Every(250);
        }

        private void OnNativeShellKeyDown(KeyDownEvent evt)
        {
            int pageDelta = GetSearchPageShortcutDelta(
                evt.keyCode,
                evt.actionKey,
                evt.shiftKey,
                evt.altKey);
            if (pageDelta != 0 && GetCurrentMainTab() == AssetInventoryTab.Search)
            {
                int targetPage = Mathf.Clamp(_curPage + pageDelta, 1, Mathf.Max(1, _pageCount));
                if (targetPage != _curPage)
                {
                    SetPage(targetPage);
                    ScheduleNativeSearchInspectorRebuild();
                }
                CommonUITK.ConsumeEvent(evt, true);
                return;
            }

            if (!evt.actionKey || evt.shiftKey || evt.altKey || evt.keyCode != KeyCode.F) return;

            ToolbarSearchField searchField = GetCurrentNativeSearchField();
            if (!FocusAndSelectSearchField(searchField)) return;

            CommonUITK.ConsumeEvent(evt, true);
        }

        private ToolbarSearchField GetCurrentNativeSearchField()
        {
            switch (GetCurrentMainTab())
            {
                case AssetInventoryTab.Search:
                    return _nativeSearchField;
                case AssetInventoryTab.Packages:
                    return _nativePackageSearchField;
                case AssetInventoryTab.Code:
                    return _nativeCodeSearchField;
                default:
                    return null;
            }
        }

        internal static int GetSearchPageShortcutDelta(
            KeyCode keyCode,
            bool actionKey,
            bool shiftKey,
            bool altKey)
        {
            if (!actionKey || shiftKey || altKey) return 0;

            switch (keyCode)
            {
                case KeyCode.PageUp:
                    return -1;
                case KeyCode.PageDown:
                    return 1;
                default:
                    return 0;
            }
        }

        internal static bool FocusAndSelectSearchField(ToolbarSearchField searchField)
        {
            if (searchField == null) return false;

            TextField textField = searchField.Q<TextField>();
            if (textField == null)
            {
                searchField.Focus();
                return true;
            }

            VisualElement textInput = textField.Q(TextField.textInputUssName);
            (textInput ?? textField).Focus();
            textField.SelectAll();
            return true;
        }

        private void RefreshUITKShell()
        {
            if (!_uitkShellActive || _mainToolbar == null || _mainBody == null) return;

            if (!_initDone)
            {
                QueueDelayedInit();
                RunDeferredInit();
            }

            AI.ResetShowAdvancedCache();
            RefreshNativeShellBlocker();

            bool showToolbar = !_nativeShellContentBlocked && _initDone && AI.IsInitialized && AI.Config != null && AI.Config.wizardCompleted && !hideMainNavigation;
            _mainToolbar.style.display = showToolbar ? DisplayStyle.Flex : DisplayStyle.None;
            _mainCustomizationBanner.style.display = showToolbar && AI.UICustomizationMode
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            bool compactToolbar = position.width < 680f;
            _mainToolbar.EnableInClassList(MainToolbarCompactClass, compactToolbar);
            if (showToolbar)
            {
                int toolbarStateHash = GetUITKToolbarStateHash(compactToolbar);
                if (_mainToolbarStateHash != toolbarStateHash)
                {
                    BuildUITKToolbar();
                    _mainToolbarStateHash = toolbarStateHash;
                }
            }
            else
            {
                _mainToolbarStateHash = int.MinValue;
            }

            RefreshUITKBodyMode();
            RefreshNativeProgressOverlay();
        }

        private VisualElement BuildUITKCustomizationBanner()
        {
            VisualElement banner = new VisualElement();
            banner.AddToClassList(MainCustomizationBannerClass);

            Label message = new Label("Customizing Standard view · Change each item where it appears.");
            message.AddToClassList(MainCustomizationMessageClass);
            banner.Add(message);

            VisualElement actions = new VisualElement();
            actions.AddToClassList(MainCustomizationActionsClass);
            actions.Add(AssetInventoryUITK.CreatePrimaryButton("Done", () =>
            {
                AI.UICustomizationMode = false;
                _mainToolbarStateHash = int.MinValue;
                MarkUITKShellDirty();
            }));
            banner.Add(actions);
            return banner;
        }

        private void RefreshNativeProgressOverlay()
        {
            if (_nativeProgressOverlay == null) return;
            int dotCount = (int)(EditorApplication.timeSinceStartup * 2d) % 4;
            if (_dragImportInProgress)
            {
                string title = "Importing" + new string('.', dotCount);
                string progressTitle = _dragImportCount > 1
                    ? $"{_dragImportMessage} ({_dragImportIndex}/{_dragImportCount})"
                    : _dragImportMessage;
                float progress = _dragImportCount > 1
                    ? Mathf.Clamp01((_dragImportIndex - 1f) / _dragImportCount)
                    : Mathf.PingPong((float)(EditorApplication.timeSinceStartup - _dragImportStartTime) * 0.35f, 1f);
                _nativeProgressOverlay.SetState(title, progressTitle, progress, "Preparing files for Unity import");
                _nativeProgressOverlay.BringToFront();
                return;
            }

            if (_pickerSelectionInProgress)
            {
                string title = "Preparing asset" + new string('.', dotCount);
                float progress = Mathf.PingPong((float)(EditorApplication.timeSinceStartup - _pickerSelectionStartTime) * 0.35f, 1f);
                _nativeProgressOverlay.SetState(
                    title,
                    _curOperation,
                    progress,
                    "Downloading, extracting, and importing can take a moment.");
                _nativeProgressOverlay.BringToFront();
                return;
            }

            _nativeProgressOverlay.Hide();
        }

        private int GetUITKToolbarStateHash(bool compactToolbar)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (compactToolbar ? 1 : 0);
                hash = hash * 31 + (int)GetCurrentMainTab();
                hash = hash * 31 + _lastTab;
                hash = hash * 31 + (_updateAvailable ? 1 : 0);
                hash = hash * 31 + (_onlineInfo?.version?.name == null ? 0 : StringComparer.Ordinal.GetHashCode(_onlineInfo.version.name));
                hash = hash * 31 + _activePackageDownloads;
                hash = hash * 31 + _availablePackageUpdates;
                hash = hash * 31 + (AI.Actions.HasPendingOrRunningActions ? 1 : 0);
                hash = hash * 31 + (_blockingInProgress ? 1 : 0);
                hash = hash * 31 + (AI.Config.hideAdvanced ? 1 : 0);
                hash = hash * 31 + (AI.UICustomizationMode ? 1 : 0);
                hash = hash * 31 + AssetInventoryUITK.GetAdvancedVisibilityStateHash();
                return hash;
            }
        }

        private void BuildUITKToolbar()
        {
            _mainToolbar.Clear();

            VisualElement mainToolbarLeft = new VisualElement();
            mainToolbarLeft.AddToClassList(MainToolbarLeftClass);
            _mainToolbar.Add(mainToolbarLeft);

            _mainTabs = new VisualElement();
            _mainTabs.AddToClassList(MainTabsClass);
            _mainToolbar.Add(_mainTabs);

            List<MainTabItem> tabs = BuildVisibleMainTabs();
            AssetInventoryTab currentTab = GetCurrentMainTab();
            for (int i = 0; i < tabs.Count; i++)
            {
                AddUITKTabButton(tabs[i], currentTab);
            }

            _mainToolbarStatus = new VisualElement();
            _mainToolbarStatus.AddToClassList(MainToolbarStatusClass);
            _mainToolbar.Add(_mainToolbarStatus);

            AddUITKToolbarStatus();
        }

        private void AddUITKTabButton(MainTabItem tab, AssetInventoryTab currentTab)
        {
            Button button = new Button(() => SelectUITKTab(tab.Tab))
            {
                text = tab.Label
            };
            button.AddToClassList(MainTabClass);
            if (tab.Tab == currentTab)
            {
                button.AddToClassList(MainTabActiveClass);
            }
            _mainTabs.Add(button);
        }

        private void AddUITKToolbarStatus()
        {
            if (_updateAvailable && _onlineInfo != null)
            {
                string releaseDate = _onlineInfo.version?.publishedDate != null ? _onlineInfo.version.publishedDate.Value.ToString() : "Unknown";
                Button update = new Button(() => AI.OpenURL(AI.ASSET_STORE_LINK))
                {
                    text = $"v{_onlineInfo.version?.name} available!"
                };
                update.tooltip = $"Released {releaseDate}";
                update.AddToClassList("ai-link-button");
                _mainToolbarStatus.Add(update);
            }

            if (_activePackageDownloads > 0)
            {
                Button downloads = AssetInventoryUITK.CreateIconButton(
                    $"{_activePackageDownloads} Downloads Active",
                    "Loading",
                    () =>
                    {
                        SelectUITKTab(AssetInventoryTab.Packages);
                        _selectedMaintenance = PackageSearch.MaintenanceOption.Downloading;
                        _requireAssetTreeRebuild = true;
                        _packageInspectorTab = 1;
                    });
                downloads.AddToClassList(MainToolbarIconClass);
                _mainToolbarStatus.Add(downloads);
            }

            if (_availablePackageUpdates > 0)
            {
                _mainToolbarStatus.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("toolbar.showupdates", () =>
                {
                    Button updates = AssetInventoryUITK.CreateIconButton(
                        $"{_availablePackageUpdates} Updates Available",
                        "preAudioLoopOff",
                        () => ShowPackageMaintenance(PackageSearch.MaintenanceOption.UpdateAvailable));
                    updates.AddToClassList(MainToolbarIconClass);
                    return updates;
                }, inlineControls: true, onVisibilityChanged: MarkUITKShellDirty));
            }

            _mainToolbarStatus.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("toolbar.reloaddatabase", () =>
            {
                bool reloadAvailable = !AI.Actions.HasPendingOrRunningActions && !_blockingInProgress;
                Button reload = AssetInventoryUITK.CreateIconButton(
                    "Reload all data from the database.",
                    "d_Refresh",
                    () =>
                    {
                        bool success = AI.Reload();
                        ShowNotification(
                            new GUIContent(success ? "Database data reloaded" : "Database data could not be reloaded"),
                            1.5f);
                    });
                reload.AddToClassList(MainToolbarIconClass);
                reload.SetEnabled(reloadAvailable);
                if (!reloadAvailable)
                {
                    reload.tooltip = "Database data cannot be reloaded while another operation is in progress.";
                }
                return reload;
            }, inlineControls: true, onVisibilityChanged: MarkUITKShellDirty));

            _mainToolbarStatus.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("toolbar.toggleadvanced", () =>
            {
                Button advanced = AssetInventoryUITK.CreateIconButton(
                    AI.Config.hideAdvanced ? "Show Advanced Features" : "Hide Advanced Features",
                    AI.Config.hideAdvanced ? "animationvisibilitytoggleoff" : "animationvisibilitytoggleon",
                    () =>
                    {
                        AI.Config.hideAdvanced = !AI.Config.hideAdvanced;
                        AI.SaveConfig();
                        RebuildNativeAboutBody();
                        MarkUITKShellDirty();
                    });
                advanced.AddToClassList(MainToolbarIconClass);
                return advanced;
            }, inlineControls: true, onVisibilityChanged: MarkUITKShellDirty));

            _mainToolbarStatus.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("toolbar.togglecustomization", () =>
            {
                Button customize = AssetInventoryUITK.CreateIconButton(
                    "Toggle UI Customization",
                    "CustomTool",
                    () =>
                    {
                        AI.UICustomizationMode = !AI.UICustomizationMode;
                        MarkUITKShellDirty();
                    });
                customize.AddToClassList(MainToolbarIconClass);
                return customize;
            }, inlineControls: true, onVisibilityChanged: MarkUITKShellDirty));

            _mainToolbarStatus.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("toolbar.toggleabout", () =>
            {
                Button about = AssetInventoryUITK.CreateIconButton("About", "_Help", ToggleUITKAbout);
                about.AddToClassList(MainToolbarIconClass);
                if (GetCurrentMainTab() == AssetInventoryTab.About)
                {
                    about.AddToClassList(MainTabActiveClass);
                }
                return about;
            }, inlineControls: true, onVisibilityChanged: MarkUITKShellDirty));
        }

        private void SelectUITKTab(AssetInventoryTab tab)
        {
            if (AI.Config == null) return;

            AssetInventoryTab current = GetCurrentMainTab();
            if (current == tab) return;

            AI.Config.tab = (int)tab;
            if (tab != AssetInventoryTab.About)
            {
                _lastTab = -1;
                AI.SaveConfig();
            }
            if (tab == AssetInventoryTab.Settings)
            {
                EditorCoroutineUtility.StartCoroutineOwnerless(UpdateStatisticsDelayed());
            }
            AudioTool.AudioManager.StopAudio();
            MarkUITKShellDirty();
        }

        private void ToggleUITKAbout()
        {
            if (AI.Config == null) return;

            if (GetCurrentMainTab() == AssetInventoryTab.About && _lastTab >= 0)
            {
                AI.Config.tab = _lastTab;
                _lastTab = -1;
            }
            else
            {
                _lastTab = AI.Config.tab;
                AI.Config.tab = (int)AssetInventoryTab.About;
            }

            MarkUITKShellDirty();
        }

        private AssetInventoryTab GetCurrentMainTab()
        {
            return Enum.IsDefined(typeof(AssetInventoryTab), AI.Config.tab)
                ? (AssetInventoryTab)AI.Config.tab
                : AssetInventoryTab.Search;
        }

        private void RefreshUITKBodyMode()
        {
            if (_nativeShellContentBlocked)
            {
                SetNativeShellContentBlocked(true);
                return;
            }

            if (IsNativeSetupShellActive())
            {
                RefreshNativeSetupBody();
            }
            if (IsNativeAboutShellActive() && _nativeAboutBody.childCount == 0)
            {
                RebuildNativeAboutBody();
            }
            if (IsNativeSearchShellActive())
            {
                RefreshNativeSearchBody();
            }
            if (IsNativePackagesShellActive())
            {
                RefreshNativePackagesBody();
            }
            if (IsNativeCodeShellActive())
            {
                RefreshNativeCodeSearchBody();
            }
            if (IsNativeReportingShellActive())
            {
                RefreshNativeReportingBody();
            }
            if (IsNativeSettingsShellActive())
            {
                RefreshNativeSettingsBody();
            }

            SetNativeShellContentBlocked(_nativeShellContentBlocked);
        }

        private bool IsNativeSetupShellActive()
        {
            return _uitkShellActive &&
                _initDone &&
                AI.IsInitialized &&
                AI.Config != null &&
                !AI.Config.wizardCompleted;
        }

        private bool IsNativeAboutShellActive()
        {
            return _uitkShellActive &&
                _initDone &&
                AI.IsInitialized &&
                AI.Config != null &&
                AI.Config.wizardCompleted &&
                !hideMainNavigation &&
                GetCurrentMainTab() == AssetInventoryTab.About;
        }

        private bool IsNativeCodeShellActive()
        {
            return _uitkShellActive &&
                _initDone &&
                AI.IsInitialized &&
                AI.Config != null &&
                AI.Config.wizardCompleted &&
                !hideMainNavigation &&
                GetCurrentMainTab() == AssetInventoryTab.Code;
        }

        private bool IsNativeSearchShellActive()
        {
            return _uitkShellActive &&
                _initDone &&
                AI.IsInitialized &&
                AI.Config != null &&
                AI.Config.wizardCompleted &&
                ((!hideMainNavigation && GetCurrentMainTab() == AssetInventoryTab.Search) ||
                    (hideMainNavigation && (searchMode || workspaceMode)));
        }

        private bool IsNativePackagesShellActive()
        {
            return _uitkShellActive &&
                _initDone &&
                AI.IsInitialized &&
                AI.Config != null &&
                AI.Config.wizardCompleted &&
                !hideMainNavigation &&
                GetCurrentMainTab() == AssetInventoryTab.Packages;
        }

        private bool IsNativeReportingShellActive()
        {
            return _uitkShellActive &&
                _initDone &&
                AI.IsInitialized &&
                AI.Config != null &&
                AI.Config.wizardCompleted &&
                !hideMainNavigation &&
                GetCurrentMainTab() == AssetInventoryTab.Reporting;
        }

        private bool IsNativeSettingsShellActive()
        {
            return _uitkShellActive &&
                _initDone &&
                AI.IsInitialized &&
                AI.Config != null &&
                AI.Config.wizardCompleted &&
                !hideMainNavigation &&
                GetCurrentMainTab() == AssetInventoryTab.Settings;
        }

        private void RefreshNativeShellBlocker()
        {
            NativeShellBlockerKind kind = GetNativeShellBlockerKind();
            int stateHash = GetNativeShellBlockerStateHash(kind);
            bool blocked = kind != NativeShellBlockerKind.None;

            if (blocked && (_nativeBlockerBody.childCount == 0 || _nativeBlockerStateHash != stateHash))
            {
                RebuildNativeShellBlocker(kind);
            }
            else if (!blocked && _nativeBlockerBody.childCount > 0)
            {
                _nativeBlockerBody.Clear();
            }

            _nativeBlockerStateHash = stateHash;
            SetNativeShellContentBlocked(blocked);
        }

        private NativeShellBlockerKind GetNativeShellBlockerKind()
        {
            if (Application.isPlaying) return NativeShellBlockerKind.PlayMode;
            if (!_initDone) return NativeShellBlockerKind.Initializing;
            if (AI.ConfigErrors != null && AI.ConfigErrors.Count > 0) return NativeShellBlockerKind.Configuration;
            if (DBAdapter.DBError != null) return NativeShellBlockerKind.Database;
            if (AI.Config != null && HasDatabaseVersionMismatchForCurrentConnection()) return NativeShellBlockerKind.DatabaseVersion;
            if (AI.UpgradeUtil != null && AI.UpgradeUtil.LongUpgradeRequired) return NativeShellBlockerKind.Upgrade;
            if (!AI.IsInitialized || AI.Config == null) return NativeShellBlockerKind.Initializing;
            return NativeShellBlockerKind.None;
        }

        private int GetNativeShellBlockerStateHash(NativeShellBlockerKind kind)
        {
            unchecked
            {
                int hash = (int)kind;
                hash = hash * 31 + (DBAdapter.DBError?.GetHashCode() ?? 0);
                hash = hash * 31 + GetCachedDatabaseVersionNumber();
                if (AI.ConfigErrors != null)
                {
                    foreach (string error in AI.ConfigErrors)
                    {
                        hash = hash * 31 + (error?.GetHashCode() ?? 0);
                    }
                }
                if (AI.UpgradeUtil != null)
                {
                    hash = hash * 31 + (AI.UpgradeUtil.CurrentMain?.GetHashCode() ?? 0);
                    hash = hash * 31 + AI.UpgradeUtil.MainProgress;
                    hash = hash * 31 + AI.UpgradeUtil.MainCount;
                    hash = hash * 31 + (AI.UpgradeUtil.CurrentSub?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }

        private void RebuildNativeShellBlocker(NativeShellBlockerKind kind)
        {
            _nativeBlockerBody.Clear();
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            VisualElement content = new VisualElement();
            content.AddToClassList(MainBlockerContentClass);
            scroll.Add(content);
            _nativeBlockerBody.Add(scroll);

            Label title = new Label(GetNativeShellBlockerTitle(kind));
            title.AddToClassList(MainBlockerTitleClass);
            content.Add(title);

            switch (kind)
            {
                case NativeShellBlockerKind.Initializing:
                    content.Add(AssetInventoryUITK.CreateHelpBox("Asset Inventory is initializing.", MessageType.Info));
                    break;
                case NativeShellBlockerKind.PlayMode:
                    content.Add(AssetInventoryUITK.CreateHelpBox("Asset Inventory is not available during Play Mode.", MessageType.Info));
                    break;
                case NativeShellBlockerKind.Configuration:
                    BuildNativeConfigurationError(content);
                    break;
                case NativeShellBlockerKind.Database:
                    BuildNativeDatabaseError(content);
                    break;
                case NativeShellBlockerKind.DatabaseVersion:
                    BuildNativeDatabaseVersionError(content);
                    break;
                case NativeShellBlockerKind.Upgrade:
                    BuildNativeUpgradeRequired(content);
                    break;
            }
        }

        private static string GetNativeShellBlockerTitle(NativeShellBlockerKind kind)
        {
            switch (kind)
            {
                case NativeShellBlockerKind.Initializing:
                    return "Asset Inventory";
                case NativeShellBlockerKind.PlayMode:
                    return "Asset Inventory Paused";
                case NativeShellBlockerKind.Upgrade:
                    return "Database Upgrade Required";
                default:
                    return "Asset Inventory Cannot Start";
            }
        }

        private void BuildNativeConfigurationError(VisualElement content)
        {
            content.Add(AssetInventoryUITK.CreateHelpBox("Configuration errors need to be fixed before Asset Inventory can start.", MessageType.Error));

            VisualElement location = AssetInventoryUITK.CreateSection("Configuration Location");
            location.Add(AssetInventoryUITK.CreateCopyLabel(AI.UsedConfigLocation));
            location.Add(CreateNativeBlockerActionRow(
                AssetInventoryUITK.CreateSecondaryButton("Open Folder", () => EditorUtility.RevealInFinder(AI.UsedConfigLocation))));
            content.Add(location);

            VisualElement errors = AssetInventoryUITK.CreateSection("Configuration Errors");
            foreach (string error in AI.ConfigErrors)
            {
                Label label = new Label(error);
                label.AddToClassList(MainBlockerErrorClass);
                errors.Add(label);
            }
            content.Add(errors);
            content.Add(CreateNativeBlockerActionRow(
                AssetInventoryUITK.CreatePrimaryButton("Reload Settings", () =>
                {
                    ResetCachedDatabaseVersionCheck();
                    AI.ReInit();
                    MarkUITKShellDirty();
                })));
        }

        private void BuildNativeDatabaseError(VisualElement content)
        {
            string dbType = AI.Config?.databaseType ?? DatabaseFactory.SQLITE;
            bool isMySQL = dbType == DatabaseFactory.MYSQL;
            content.Add(AssetInventoryUITK.CreateHelpBox(
                isMySQL
                    ? "The database connection failed. Check the MySQL server settings and credentials."
                    : "The database could not be opened and may be corrupted. A failed network-drive synchronization can also cause this problem.",
                MessageType.Error));

            VisualElement info = AssetInventoryUITK.CreateSection("Database Information");
            info.Add(AssetInventoryUITK.CreateKeyValueRow("Database Type", isMySQL ? "MySQL" : "SQLite"));
            if (isMySQL)
            {
                info.Add(AssetInventoryUITK.CreateKeyValueRow("Host", $"{AI.Config.mysqlHost}:{AI.Config.mysqlPort}"));
                info.Add(AssetInventoryUITK.CreateKeyValueRow("Database", AI.Config.mysqlDatabase));
            }
            else
            {
                info.Add(AssetInventoryUITK.CreateCopyLabel(DBAdapter.GetDBPath()));
                info.Add(CreateNativeBlockerActionRow(
                    AssetInventoryUITK.CreateSecondaryButton("Open Folder", () => EditorUtility.RevealInFinder(DBAdapter.GetDBPath()))));
            }
            content.Add(info);

            VisualElement details = AssetInventoryUITK.CreateSection("Error Details");
            Label error = new Label(DBAdapter.DBError ?? "Unknown database error");
            error.AddToClassList(MainBlockerErrorClass);
            details.Add(error);
            content.Add(details);

            List<VisualElement> actions = new List<VisualElement>
            {
                AssetInventoryUITK.CreatePrimaryButton("Configure Database...", () => DatabaseConfigurationUI.ShowWindow()),
                AssetInventoryUITK.CreateSecondaryButton("Retry Connection", RetryNativeDatabaseConnection)
            };
            if (!isMySQL)
            {
                actions.Add(AssetInventoryUITK.CreateDestructiveButton("Delete Database & Retry", DeleteNativeDatabaseAndRetry));
            }
            content.Add(CreateNativeBlockerActionRow(actions.ToArray()));
        }

        private void BuildNativeDatabaseVersionError(VisualElement content)
        {
            int version = GetCachedDatabaseVersionNumber();
            content.Add(AssetInventoryUITK.CreateHelpBox(
                $"This database uses version {version}, but this Asset Inventory version supports up to {UpgradeUtil.CURRENT_DB_VERSION}. Update Asset Inventory before continuing.",
                MessageType.Error));

            VisualElement info = AssetInventoryUITK.CreateSection("Database Information");
            info.Add(AssetInventoryUITK.CreateKeyValueRow("Database Version", version > 0 ? version.ToString() : "Unknown"));
            info.Add(AssetInventoryUITK.CreateKeyValueRow("Supported Version", UpgradeUtil.CURRENT_DB_VERSION.ToString()));
            bool isMySQL = (AI.Config.databaseType ?? DatabaseFactory.SQLITE) == DatabaseFactory.MYSQL;
            info.Add(AssetInventoryUITK.CreateKeyValueRow("Database Type", isMySQL ? "MySQL" : "SQLite"));
            if (isMySQL)
            {
                info.Add(AssetInventoryUITK.CreateKeyValueRow("Host", $"{AI.Config.mysqlHost}:{AI.Config.mysqlPort}"));
                info.Add(AssetInventoryUITK.CreateKeyValueRow("Database", AI.Config.mysqlDatabase));
            }
            else
            {
                info.Add(AssetInventoryUITK.CreateCopyLabel(DBAdapter.GetDBPath()));
                info.Add(CreateNativeBlockerActionRow(
                    AssetInventoryUITK.CreateSecondaryButton("Open Folder", () => EditorUtility.RevealInFinder(DBAdapter.GetDBPath()))));
            }
            content.Add(info);
            content.Add(CreateNativeBlockerActionRow(
                AssetInventoryUITK.CreatePrimaryButton("Retry Connection", RetryNativeDatabaseConnection)));
        }

        private void BuildNativeUpgradeRequired(VisualElement content)
        {
            content.Add(AssetInventoryUITK.CreateHelpBox(
                "A longer or incompatible database upgrade is required. Back up the database before continuing.",
                MessageType.Warning));

            VisualElement pending = AssetInventoryUITK.CreateSection("Pending Upgrades");
            IReadOnlyList<string> upgrades = AI.UpgradeUtil.PendingLongUpgrades;
            for (int i = 0; i < upgrades.Count; i++)
            {
                pending.Add(AssetInventoryUITK.CreateKeyValueRow((i + 1).ToString(), GetNativeUpgradeDescription(upgrades[i])));
            }
            content.Add(pending);

            Button start = AssetInventoryUITK.CreatePrimaryButton("Start Upgrade Process", AI.UpgradeUtil.StartLongRunningUpgrades);
            start.SetEnabled(string.IsNullOrEmpty(AI.UpgradeUtil.CurrentMain));
            content.Add(CreateNativeBlockerActionRow(start));

            if (!string.IsNullOrEmpty(AI.UpgradeUtil.CurrentMain))
            {
                float progress = AI.UpgradeUtil.MainCount > 0
                    ? AI.UpgradeUtil.MainProgress / (float)AI.UpgradeUtil.MainCount
                    : 0f;
                content.Add(AssetInventoryUITK.CreateProgressBar(
                    string.IsNullOrEmpty(AI.UpgradeUtil.CurrentSub)
                        ? AI.UpgradeUtil.CurrentMain
                        : $"{AI.UpgradeUtil.CurrentMain} - {AI.UpgradeUtil.CurrentSub}",
                    progress));
            }
        }

        private static string GetNativeUpgradeDescription(string upgrade)
        {
            switch ((upgrade ?? string.Empty).ToLowerInvariant())
            {
                case "previewconversion":
                    return "Upgrade preview image storage";
                case "assetcacheconversion":
                    return "Make cache paths portable and normalize path separators";
                case "custompackagedates":
                    return "Populate release dates for custom packages";
                case "cachefolderversions":
                    return "Add version information to cache folder names";
                default:
                    return upgrade ?? "Unknown upgrade";
            }
        }

        private static VisualElement CreateNativeBlockerActionRow(params VisualElement[] actions)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(MainBlockerActionsClass);
            if (actions != null)
            {
                foreach (VisualElement action in actions)
                {
                    if (action != null) row.Add(action);
                }
            }
            return row;
        }

        private void RetryNativeDatabaseConnection()
        {
            ResetCachedDatabaseVersionCheck();
            DBAdapter.Close();
            AI.ReInit();
            MarkUITKShellDirty();
        }

        private void DeleteNativeDatabaseAndRetry()
        {
            string path = DBAdapter.GetDBPath();
            ResetCachedDatabaseVersionCheck();
            DBAdapter.Close();
            if (File.Exists(path)) File.Delete(path);
            AI.ReInit();
            MarkUITKShellDirty();
        }

        private void RebuildNativeAboutBody()
        {
            if (_nativeAboutBody == null) return;

            _nativeAboutBody.Clear();
            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1f;
            scrollView.Add(AboutWindow.CreateContent("AssetInventory", AddNativeAssetInventoryAboutSection));
            _nativeAboutBody.Add(scrollView);
        }

        private void AddNativeAssetInventoryAboutSection(VisualElement root)
        {
#if !USE_TUTORIALS
            VisualElement tutorials = CreateNativeAboutPanel();
            tutorials.Add(CreateNativeAboutTitle("Tutorials"));
            tutorials.Add(CreateNativeAboutCopy("Integrated tutorials require the Unity Tutorials package."));
            VisualElement tutorialButtons = CreateNativeAboutButtonRow();
            tutorialButtons.Add(AssetInventoryUITK.CreateSecondaryButton(
                "Install/Upgrade Tutorials Package...",
                () => Client.Add($"com.unity.learn.iet-framework@{AI.TUTORIALS_VERSION}")));
            tutorials.Add(tutorialButtons);
            root.Add(tutorials);
#endif

            if (ShowAdvanced())
            {
                VisualElement maintenance = CreateNativeAboutPanel();
                maintenance.Add(CreateNativeAboutTitle("Maintenance"));
                VisualElement maintenanceButtons = CreateNativeAboutButtonRow();
                maintenanceButtons.Add(AssetInventoryUITK.CreateSecondaryButton("Show Welcome Dialog", WelcomeWindow.ShowWindow));
                maintenanceButtons.Add(AssetInventoryUITK.CreateSecondaryButton(
                    "Restart Setup Wizard",
                    () =>
                    {
                        AI.Config.wizardCompleted = false;
                        AI.Config.wizardCurrentPage = 0;
                        AI.SaveConfig();
                        MarkUITKShellDirty();
                    }));
                maintenanceButtons.Add(AssetInventoryUITK.CreateSecondaryButton("Create Debug Support Report", CreateDebugReport));
                maintenance.Add(maintenanceButtons);
                root.Add(maintenance);
            }

            if (AI.DEBUG_MODE)
            {
                VisualElement debug = CreateNativeAboutPanel();
                debug.Add(CreateNativeAboutTitle("Debug"));
                VisualElement debugButtons = CreateNativeAboutButtonRow();
                debugButtons.Add(AssetInventoryUITK.CreateSecondaryButton("Reload Lookups", () => ReloadLookups()));
                debugButtons.Add(AssetInventoryUITK.CreateSecondaryButton("Get Token", () => Debug.Log(CloudProjectSettings.accessToken)));
                debugButtons.Add(AssetInventoryUITK.CreateSecondaryButton("Free Memory", () => Resources.UnloadUnusedAssets()));
                debug.Add(debugButtons);
                root.Add(debug);
            }
        }

        private static VisualElement CreateNativeAboutPanel()
        {
            VisualElement panel = new VisualElement();
            panel.AddToClassList(AboutPanelClass);
            return panel;
        }

        private static Label CreateNativeAboutTitle(string text)
        {
            Label title = new Label(text);
            title.AddToClassList(AboutPanelTitleClass);
            return title;
        }

        private static Label CreateNativeAboutCopy(string text)
        {
            Label copy = new Label(text);
            copy.AddToClassList(AboutPanelCopyClass);
            return copy;
        }

        private static VisualElement CreateNativeAboutButtonRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(AboutButtonRowClass);
            return row;
        }

        private void SetNativeShellContentBlocked(bool blocked)
        {
            _nativeShellContentBlocked = blocked;
            if (_nativeBlockerBody != null)
            {
                _nativeBlockerBody.style.display = blocked ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_nativeSetupBody != null)
            {
                _nativeSetupBody.style.display = IsNativeSetupShellActive() && !blocked
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_nativeSearchBody != null)
            {
                _nativeSearchBody.style.display = IsNativeSearchShellActive() && !blocked
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_nativePackagesBody != null)
            {
                _nativePackagesBody.style.display = IsNativePackagesShellActive() && !blocked
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_nativeAboutBody != null)
            {
                _nativeAboutBody.style.display = IsNativeAboutShellActive() && !blocked
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_nativeCodeBody != null)
            {
                _nativeCodeBody.style.display = IsNativeCodeShellActive() && !blocked
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_nativeReportingBody != null)
            {
                _nativeReportingBody.style.display = IsNativeReportingShellActive() && !blocked
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_nativeSettingsBody != null)
            {
                _nativeSettingsBody.style.display = IsNativeSettingsShellActive() && !blocked
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
        }

        private void MarkUITKShellDirty()
        {
            RefreshUITKShell();
            Repaint();
        }
    }
}
