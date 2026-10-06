using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class RelativeUI : EditorWindow
    {
        private const int BREAK_INTERVAL = 50;

        private string _key;
        private bool _conversionRunning;
        private Action<string> _callback;
        private bool _disableMode;
        private string _location;
        private FolderSpec _spec;
        private int _conversionCount;
        private int _currentConversion;
        private string _conversionText;
        private RelativeLocation _relLocation;
        private HashSet<string> _locations;
        private int _locationIdx;
        private string[] _locationsArr;
        private ProgressBar _progressBar;
        private IVisualElementScheduledItem _progressUpdate;

        public static RelativeUI ShowWindow()
        {
            RelativeUI window = GetWindow<RelativeUI>("Relative Storage");
            window.minSize = new Vector2(200, 250);
            window.maxSize = new Vector2(1000, 250);

            return window;
        }

        public void Init(FolderSpec spec)
        {
            _spec = spec;
            _disableMode = spec.location.StartsWith(AI.TAG_START);
            _key = _disableMode ? spec.relativeKey : Path.GetFileName(spec.location);
            _conversionRunning = false;
            _locationIdx = 0;

            if (_disableMode)
            {
                _relLocation = AI.RelativeLocations.FirstOrDefault(rl => rl.Key == _spec.relativeKey);
                _location = _relLocation?.Location;
                _locations = new HashSet<string>();
                if (!string.IsNullOrWhiteSpace(_location)) _locations.Add(ConvertSlashToUnicodeSlash(_location));
                _relLocation?.otherLocations.ForEach(rl => _locations.Add(ConvertSlashToUnicodeSlash(rl)));
                _locationsArr = _locations.ToArray();
            }

            Build();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void OnDisable()
        {
            _progressUpdate?.Pause();
            _progressUpdate = null;
        }

        private void Build()
        {
            _progressUpdate?.Pause();
            _progressUpdate = null;
            _progressBar = null;

            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_spec == null || string.IsNullOrWhiteSpace(_spec.location))
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Select a folder before configuring relative storage.", MessageType.Info));
                return;
            }

            root.Add(AssetInventoryUITK.CreateHelpBox(
                "Relative storage lets the same database work across multiple devices that map the source files to different folders or drive structures.",
                MessageType.None));

            if (_disableMode)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Revert Relative Storage");
                section.Add(AssetInventoryUITK.CreateCopyLabel("Reverting replaces all usages of the key with the selected original location again. That may break usage from other systems if they rely on the relative mapping."));

                section.Add(AssetInventoryUITK.CreateKeyValueRow("Key", _spec.relativeKey));

                if (_relLocation != null)
                {
                    List<string> locations = _locationsArr?.ToList() ?? new List<string>();
                    if (locations.Count > 0)
                    {
                        _locationIdx = Mathf.Clamp(_locationIdx, 0, locations.Count - 1);
                        PopupField<string> locationPopup = new PopupField<string>(locations, _locationIdx);
                        locationPopup.RegisterValueChangedCallback(evt => _locationIdx = locations.IndexOf(evt.newValue));
                        locationPopup.SetEnabled(!_conversionRunning);
                        section.Add(AssetInventoryUITK.CreateFieldRow("Location to Restore", locationPopup));
                    }

                    int affectedSystems = _relLocation.otherLocations.Count + (_relLocation.Id > 0 ? 1 : 0);
                    section.Add(AssetInventoryUITK.CreateKeyValueRow("Affected Systems", affectedSystems.ToString()));
                }
                else
                {
                    section.Add(AssetInventoryUITK.CreateHelpBox("No relative location mapping could be found for the key. Reversing the database is not possible.", MessageType.Error));
                }

                section.SetEnabled(!_conversionRunning);
                root.Add(section);
            }
            else
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Enable Relative Storage");
                section.Add(AssetInventoryUITK.CreateCopyLabel("Conversion replaces absolute base paths with a key. Other devices can then map that key to their own local folder."));

                section.Add(AssetInventoryUITK.CreateHelpBox("The conversion will update all indexed assets matching this location. It can be reverted later.", MessageType.Info));
                section.Add(AssetInventoryUITK.CreateKeyValueRow("Location", _spec.location));

                TextField keyField = new TextField
                {
                    value = _key
                };
                keyField.RegisterValueChangedCallback(evt => _key = evt.newValue);
                keyField.SetEnabled(!_conversionRunning);
                section.Add(AssetInventoryUITK.CreateFieldRow("Key", keyField));
                section.SetEnabled(!_conversionRunning);
                root.Add(section);
            }

            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            if (_conversionRunning)
            {
                VisualElement progressRow = new VisualElement();
                progressRow.AddToClassList("ai-progress-row");
                _progressBar = AssetInventoryUITK.CreateProgressBar(GetProgressLabel(), GetProgressValue());
                progressRow.Add(_progressBar);
                root.Add(progressRow);
                _progressUpdate = root.schedule.Execute(RefreshProgress).Every(250);
            }
            else
            {
                VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
                if (_disableMode)
                {
                    Button button = AssetInventoryUITK.CreatePrimaryButton("Revert Relative Persistence", RevertRelative);
                    button.SetEnabled(_relLocation != null);
                    footer.Add(button);
                }
                else
                {
                    Button button = AssetInventoryUITK.CreatePrimaryButton("Start Conversion", MakeRelative);
                    button.SetEnabled(!string.IsNullOrWhiteSpace(_key));
                    footer.Add(button);
                }
                root.Add(footer);
            }
        }

        private void RefreshProgress()
        {
            if (!_conversionRunning || _progressBar == null) return;

            _progressBar.title = GetProgressLabel();
            _progressBar.value = GetProgressValue();
        }

        private string GetProgressLabel()
        {
            if (string.IsNullOrWhiteSpace(_conversionText)) return "Conversion in progress";
            return $"{_conversionText}: {Mathf.Min(_currentConversion + 1, _conversionCount):N0}/{_conversionCount:N0}";
        }

        private float GetProgressValue()
        {
            if (_conversionCount <= 0) return 0f;
            return Mathf.Clamp01(_currentConversion / (float)_conversionCount);
        }

        private async void MakeRelative()
        {
            if (new[] {"ac", "pc"}.Contains(_key.ToLowerInvariant()))
            {
                EditorUtility.DisplayDialog("Invalid key", "The key cannot be 'ac' or 'pc' as these are reserved for the Asset and Package cache.", "OK");
                return;
            }

            _conversionRunning = true;
            Build();

            // create configuration
            RelativeLocation rel = new RelativeLocation();
            rel.System = AI.GetSystemId();
            rel.Key = _key;
            rel.SetLocation(_spec.location);
            DBAdapter.DB.Insert(rel);

            // adapt all folder specs with that location since it is not unique to know exactly which folder resulted in which asset entry
            AI.Config.folders.Where(f => f.location == rel.Location).ForEach(f =>
            {
                f.storeRelative = true;
                f.relativeKey = _key;
                f.location = $"{AI.TAG_START}{_key}{AI.TAG_END}";
            });
            AI.SaveConfig();
            Paths.LoadRelativeLocations();

            // fetch assets in question
            string dbKey = $"{AI.TAG_START}{_key}{AI.TAG_END}";
            List<Asset> assets = DBAdapter.DB.Query<Asset>("SELECT Id, Location from Asset where Location like ?", rel.Location + "%");
            _conversionCount = assets.Count;
            _conversionText = "Packages done";
            for (_currentConversion = 0; _currentConversion < _conversionCount; _currentConversion++)
            {
                string newLocation = assets[_currentConversion].Location.Replace(rel.Location, dbKey);
                DBAdapter.DB.Execute("UPDATE Asset set Location = ? where Id = ?", newLocation, assets[_currentConversion].Id);
            }

            List<AssetFile> files = DBAdapter.DB.Query<AssetFile>("SELECT Id, Path, SourcePath from AssetFile where Path like ?", rel.Location + "%");
            _conversionCount = files.Count;
            _conversionText = "Files done";
            for (_currentConversion = 0; _currentConversion < _conversionCount; _currentConversion++)
            {
                string newPath = files[_currentConversion].Path.Replace(rel.Location, dbKey);
                string newSourcePath = files[_currentConversion].SourcePath.Replace(rel.Location, dbKey);
                DBAdapter.DB.Execute("UPDATE AssetFile set Path = ?, SourcePath = ? where Id = ?", newPath, newSourcePath, files[_currentConversion].Id);
                if (_currentConversion % BREAK_INTERVAL == 0) await Task.Yield(); // let editor breath in case many files are already indexed
            }

            _conversionRunning = false;

            CloseWindow();
        }

        private async void RevertRelative()
        {
            _conversionRunning = true;
            Build();
            _location = ConvertUnicodeSlashToSlash(_locationsArr[_locationIdx]).TrimEnd('/');

            // adapt all folder specs with that location since it is not unique to know exactly which folder resulted in which asset entry
            AI.Config.folders.Where(f => f.relativeKey == _key).ForEach(f =>
            {
                f.storeRelative = false;
                f.relativeKey = null;
                f.location = _location;
            });
            AI.SaveConfig();

            int keyUsages = AI.Config.folders.Count(fs => fs.relativeKey == _key);
            if (keyUsages == 0)
            {
                DBAdapter.DB.Execute("DELETE from RelativeLocation where Key=?", _key);
            }
            Paths.LoadRelativeLocations();

            // fetch assets in question
            string dbKey = $"{AI.TAG_START}{_key}{AI.TAG_END}";
            List<Asset> assets = DBAdapter.DB.Query<Asset>("SELECT Id, Location from Asset where Location like ?", dbKey + "%");
            _conversionCount = assets.Count;
            _conversionText = "Packages done";
            for (_currentConversion = 0; _currentConversion < _conversionCount; _currentConversion++)
            {
                string newLocation = assets[_currentConversion].Location.Replace(dbKey, _location);
                DBAdapter.DB.Execute("UPDATE Asset set Location = ? where Id = ?", newLocation, assets[_currentConversion].Id);
            }

            List<AssetFile> files = DBAdapter.DB.Query<AssetFile>("SELECT Id, Path, SourcePath from AssetFile where Path like ?", dbKey + "%");
            _conversionCount = files.Count;
            _conversionText = "Files done";
            for (_currentConversion = 0; _currentConversion < _conversionCount; _currentConversion++)
            {
                string newPath = files[_currentConversion].Path.Replace(dbKey, _location);
                string newSourcePath = files[_currentConversion].SourcePath.Replace(dbKey, _location);
                DBAdapter.DB.Execute("UPDATE AssetFile set Path = ?, SourcePath = ? where Id = ?", newPath, newSourcePath, files[_currentConversion].Id);
                if (_currentConversion % BREAK_INTERVAL == 0) await Task.Yield(); // let editor breath in case many files are already indexed
            }

            _conversionRunning = false;

            CloseWindow();
        }

        private void CloseWindow()
        {
            AI.TriggerPackageRefresh();
            Close();
        }

        private string ConvertSlashToUnicodeSlash(string text)
        {
            return text.Replace('/', '\u2215');
        }

        private string ConvertUnicodeSlashToSlash(string text)
        {
            return text.Replace('\u2215', '/');
        }

        private void OnInspectorUpdate()
        {
            RefreshProgress();
        }
    }
}
