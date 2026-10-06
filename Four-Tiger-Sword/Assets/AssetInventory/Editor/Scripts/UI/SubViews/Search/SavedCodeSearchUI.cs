using System;
using UnityEditor;

namespace AssetInventory
{
    public sealed class SavedCodeSearchUI : BaseSavedSearchUI<SavedCodeSearch>
    {
        public static SavedCodeSearchUI ShowWindow()
        {
            SavedCodeSearchUI window = GetWindow<SavedCodeSearchUI>("Saved Code Search Editor");
            window.minSize = new UnityEngine.Vector2(400, 250);
            return window;
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        public void Init(SavedCodeSearch savedSearch, Action<SavedCodeSearch> onSave = null)
        {
            InitSavedSearch(savedSearch, onSave);
        }

        protected override string GetName()
        {
            return _savedSearch.Name;
        }

        protected override void SetName(string searchName)
        {
            _savedSearch.Name = searchName;
        }

        protected override string GetIcon()
        {
            return _savedSearch.Icon;
        }

        protected override void SetIcon(string icon)
        {
            _savedSearch.Icon = icon;
        }

        protected override string GetColor()
        {
            return _savedSearch.Color;
        }

        protected override void SetColor(string color)
        {
            _savedSearch.Color = color;
        }

        protected override string GetSearchPhrase()
        {
            return _savedSearch.SearchPhrase;
        }

        protected override string GetSearchDetails()
        {
            return string.Empty;
        }

        protected override void UpdateDatabase()
        {
            DBAdapter.DB.Update(_savedSearch);
        }
    }
}
