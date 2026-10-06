using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class RemovalUI : EditorWindow
    {
        public static event Action OnUninstallDone;

        private List<AssetInfo> _assets;
        private bool _running;
        private bool _cancellationRequested;
        private RemoveRequest _removeRequest;
        private AssetInfo _curInfo;
        private int _queueCount;
        private Action _callback;
        private IVisualElementScheduledItem _statusUpdate;
        private readonly Dictionary<AssetInfo, Label> _statePills = new Dictionary<AssetInfo, Label>();

        public static RemovalUI ShowWindow()
        {
            RemovalUI window = GetWindow<RemovalUI>("Removal Wizard");
            window.minSize = new Vector2(450, 200);

            return window;
        }

        public void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        public void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            _statusUpdate?.Pause();
            _statusUpdate = null;
        }

        private void OnBeforeAssemblyReload()
        {
            // right now not any state to persist actually, Unity will serialize the whole view correctly
        }

        private void OnAfterAssemblyReload()
        {
            if (_running)
            {
                // means there was an interactive import active which triggered a recompile, so let's continue
                BulkRemoveAssets(false);
            }
        }

        public void Init(List<AssetInfo> assets, Action callback = null, bool autoStart = true, bool resetImportState = true)
        {
            _callback = callback;
            _assets = assets.Where(a => a.ParentId == 0)
                .OrderByDescending(a => a.AssetSource).ThenBy(a => a.GetDisplayName())
                .ToArray().ToList(); // break direct reference so that package list refresh does not clear import state

            // check if only sub-packages were selected, this is a valid scenario
            if (_assets.Count == 0)
            {
                _assets = assets.Where(a => a.ParentId > 0)
                    .OrderByDescending(a => a.AssetSource).ThenBy(a => a.GetDisplayName())
                    .ToArray().ToList(); // break direct reference so that package list refresh does not clear import state
            }

            _queueCount = 0;
            foreach (AssetInfo info in _assets)
            {
                if (info.SafeName == Asset.NONE) continue;

                if (resetImportState) info.ImportState = AssetInfo.ImportStateOptions.Queued;
                _queueCount++;
            }
            if (autoStart) BulkRemoveAssets(false);
            Build();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);
            _statePills.Clear();

            if (_assets == null || _assets.Count == 0)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Select packages in the Asset Inventory for removal first.", MessageType.Info));
                StopStatusRefresh();
                return;
            }

            root.Add(BuildSummarySection());
            root.Add(BuildQueueSection());
            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            root.Add(BuildFooter());

            if (_running)
            {
                StartStatusRefresh();
            }
            else
            {
                StopStatusRefresh();
            }
        }

        private VisualElement BuildSummarySection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Queue");
            section.Add(AssetInventoryUITK.CreateKeyValueRow("Packages", _assets.Count.ToString("N0")));
            section.Add(AssetInventoryUITK.CreateKeyValueRow("Removable", _queueCount.ToString("N0")));
            return section;
        }

        private VisualElement BuildQueueSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Packages");
            section.AddToClassList("ai-removal-queue-section");

            ScrollView list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ai-list");
            list.AddToClassList("ai-removal-list");

            int rowIndex = 0;
            foreach (AssetInfo info in _assets)
            {
                if (info.SafeName == Asset.NONE) continue;

                VisualElement row = new VisualElement();
                string subtitle = GetPackageSubtitle(info);
                AssetInventoryUITK.PopulateListRow(
                    row,
                    info.GetDisplayName(),
                    subtitle,
                    trailing: CreateStatePill(info),
                    extraClasses: rowIndex % 2 == 1
                        ? new[] {"ai-removal-row", BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName}
                        : new[] {"ai-removal-row"});
                row.Q<Label>("title").tooltip = info.SafeName;
                row.Q<Label>("subtitle").tooltip = subtitle;
                list.Add(row);
                rowIndex++;
            }

            section.Add(list);
            return section;
        }

        private VisualElement BuildFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();

            Button remove = AssetInventoryUITK.CreatePrimaryButton("Remove", () => BulkRemoveAssets(true));
            remove.tooltip = "Start removal process.";
            remove.SetEnabled(!_running);
            footer.Add(remove);

            if (_running)
            {
                Button cancel = AssetInventoryUITK.CreateSecondaryButton("Cancel All", () =>
                {
                    _cancellationRequested = true; // will not always work if there was a recompile in between
                    _running = false;
                    Build();
                });
                footer.Add(cancel);
            }

            return footer;
        }

        private static string GetPackageSubtitle(AssetInfo info)
        {
            if (info.AssetSource == Asset.Source.RegistryPackage)
            {
                string version = info.InstalledPackageVersion();
                return version != null ? $"{info.SafeName} - {version}" : $"{info.SafeName} - checking installed version";
            }

            string location = info.GetLocation(true);
            return string.IsNullOrWhiteSpace(location) ? info.SafeName : location;
        }

        private Label CreateStatePill(AssetInfo info)
        {
            Label pill = AssetInventoryUITK.CreateStatusPill(string.Empty);
            pill.AddToClassList("ai-removal-state-pill");
            ApplyStatePill(pill, info.ImportState);
            _statePills[info] = pill;
            return pill;
        }

        private static void ApplyStatePill(Label pill, AssetInfo.ImportStateOptions state)
        {
            pill.text = StringUtils.CamelCaseToWords(state.ToString());
            pill.EnableInClassList("ai-status-success", state == AssetInfo.ImportStateOptions.Uninstalled);
            pill.EnableInClassList("ai-status-progress", state == AssetInfo.ImportStateOptions.Uninstalling);
            pill.EnableInClassList("ai-status-error", state == AssetInfo.ImportStateOptions.Failed);
            pill.EnableInClassList("ai-status-warning", state == AssetInfo.ImportStateOptions.Cancelled);
            pill.EnableInClassList("ai-status-pending", GetStatusClass(state) == "ai-status-pending");
        }

        private static string GetStatusClass(AssetInfo.ImportStateOptions state)
        {
            switch (state)
            {
                case AssetInfo.ImportStateOptions.Uninstalled:
                    return "ai-status-success";
                case AssetInfo.ImportStateOptions.Uninstalling:
                    return "ai-status-progress";
                case AssetInfo.ImportStateOptions.Failed:
                    return "ai-status-error";
                case AssetInfo.ImportStateOptions.Cancelled:
                    return "ai-status-warning";
                default:
                    return "ai-status-pending";
            }
        }

        private void StartStatusRefresh()
        {
            if (_statusUpdate != null) return;
            _statusUpdate = rootVisualElement.schedule.Execute(RefreshStatusRows).Every(250);
        }

        private void StopStatusRefresh()
        {
            _statusUpdate?.Pause();
            _statusUpdate = null;
        }

        private void RefreshStatusRows()
        {
            if (_assets == null) return;

            foreach (AssetInfo info in _assets)
            {
                if (_statePills.TryGetValue(info, out Label pill))
                {
                    ApplyStatePill(pill, info.ImportState);
                }
            }
        }

        private async void BulkRemoveAssets(bool resetState)
        {
            if (resetState)
            {
                _assets
                    .Where(a => a.ImportState == AssetInfo.ImportStateOptions.Cancelled || a.ImportState == AssetInfo.ImportStateOptions.Failed)
                    .ForEach(a => a.ImportState = AssetInfo.ImportStateOptions.Queued);
            }

            // importing will be set if there was a recompile during an ongoing import
            IEnumerable<AssetInfo> removalQueue = _assets.Where(a => a.ImportState == AssetInfo.ImportStateOptions.Queued || a.ImportState == AssetInfo.ImportStateOptions.Uninstalling)
                .Where(a => a.SafeName != Asset.NONE)
                .ToList();
            if (removalQueue.Count() == 0) return;

            _running = true;
            _cancellationRequested = false;
            Build();

            await DoBulkRemoval(removalQueue, true);
            bool allDone = removalQueue.All(a => a.ImportState == AssetInfo.ImportStateOptions.Uninstalled);
            _running = false;
            Build();

            OnUninstallDone?.Invoke();

            // custom one-time callback handler
            _callback?.Invoke();
            _callback = null;

            if (allDone) Close();
        }

        private async Task DoBulkRemoval(IEnumerable<AssetInfo> queue, bool allAutomatic)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (AssetInfo info in queue)
                {
                    _curInfo = info;

                    if (info.ImportState != AssetInfo.ImportStateOptions.Uninstalling)
                    {
                        info.ImportState = AssetInfo.ImportStateOptions.Uninstalling;

                        if (info.AssetSource == Asset.Source.RegistryPackage)
                        {
                            _removeRequest = RemovePackage(info);
                            if (_removeRequest == null) continue;

                            EditorApplication.update += RemoveProgress;
                        }
                    }

                    // wait until done
                    while (!_cancellationRequested && info.ImportState == AssetInfo.ImportStateOptions.Uninstalling)
                    {
                        await Task.Delay(25);
                    }

                    if (info.ImportState == AssetInfo.ImportStateOptions.Uninstalling) info.ImportState = AssetInfo.ImportStateOptions.Queued;
                    if (_cancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error uninstalling packages: {e.Message}");
            }

            // handle potentially pending uninstalls and put them back in the queue
            _assets.ForEach(info =>
            {
                if (info.ImportState == AssetInfo.ImportStateOptions.Uninstalling) info.ImportState = AssetInfo.ImportStateOptions.Queued;
            });

            if (allAutomatic)
            {
                // set inactive since the next line will trigger a recompile and will otherwise continue the import
                _running = false;
            }

            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
            Client.Resolve();
        }

        private static RemoveRequest RemovePackage(AssetInfo info)
        {
            if (info.PackageSource == PackageSource.Embedded)
            {
                // embedded packages need to be deleted manually
                PackageInfo pInfo = AssetStore.GetPackageInfo(info);
                FileUtil.DeleteFileOrDirectory(pInfo.resolvedPath);
                AssetDatabase.Refresh();
                return null;
            }
            return Client.Remove(info.SafeName);
        }

        private void RemoveProgress()
        {
            if (!_removeRequest.IsCompleted) return;

            EditorApplication.update -= RemoveProgress;

            if (_removeRequest.Status == StatusCode.Success)
            {
                _curInfo.ImportState = AssetInfo.ImportStateOptions.Uninstalled;
            }
            else
            {
                _curInfo.ImportState = AssetInfo.ImportStateOptions.Failed;
                Debug.LogError($"Uninstalling {_curInfo} failed: {_removeRequest.Error.message}");
            }
            RefreshStatusRows();
        }

        private void OnInspectorUpdate()
        {
            if (_running) RefreshStatusRows();
        }
    }
}
