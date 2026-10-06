using System;
using Brain;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class AdvancedSearchUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(410f, 360f);

        private Action<string, string> _onSearchSelection;
        private string _phrase = "Images with at least 1000 pixels in width but only if they contain the word 'nature'";

        public static AdvancedSearchUI ShowDropdown(Rect anchor, Action<string, string> onSearchSelection)
        {
            AdvancedSearchUI window = CreateInstance<AdvancedSearchUI>();
            window.titleContent = new GUIContent("Example Searches");
            window.minSize = WindowSize;
            window.Init(onSearchSelection);
            AssetInventoryUITK.ShowAsDropDown(window, anchor, WindowSize);
            return window;
        }

        public static AdvancedSearchUI ShowDropdown(EditorWindow owner, VisualElement anchor, Action<string, string> onSearchSelection)
        {
            AdvancedSearchUI window = CreateInstance<AdvancedSearchUI>();
            window.titleContent = new GUIContent("Example Searches");
            window.minSize = WindowSize;
            window.Init(onSearchSelection);
            AssetInventoryUITK.ShowAsDropDown(window, owner, anchor, WindowSize);
            return window;
        }

        public static AdvancedSearchUI ShowWindow(Action<string, string> onSearchSelection = null)
        {
            AdvancedSearchUI window = GetWindow<AdvancedSearchUI>("Example Searches");
            window.minSize = WindowSize;
            window.Init(onSearchSelection);
            return window;
        }

        public void Init(Action<string, string> onSearchSelection)
        {
            _onSearchSelection = onSearchSelection;
            if (rootVisualElement != null && rootVisualElement.childCount > 0)
            {
                BuildContent();
            }
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            ScrollView scrollView = new ScrollView();
            scrollView.AddToClassList("ai-example-search-scroll");

            scrollView.Add(BuildSearchSection(
                "Simple Searches",
                new SearchExample("'Car' prefabs", "car", "Prefabs"),
                new SearchExample("'Books' but not 'book shelves' or 'bookmarks'", "book -shelf -mark", "Prefabs"),
                new SearchExample("Search for an exact phrase", "~book shelf")));

            scrollView.Add(BuildSearchSection(
                "Advanced Searches",
                new SearchExample("Results from free packages only", "=AssetFile.FileName like \"%TEXT%\" and Asset.PriceEur = 0"),
                new SearchExample("Audio files between 10-20 seconds in length", "=AssetFile.Length >= 10 and AssetFile.Length <= 20", "Audio"),
                new SearchExample("Files with an AI caption available", "=AssetFile.AICaption is not null"),
                new SearchExample("Previews scheduled for recreation", "=AssetFile.PreviewState=2 OR AssetFile.PreviewState=6")));

            scrollView.Add(BuildAISearchSection());
            root.Add(scrollView);
        }

        private VisualElement BuildSearchSection(string title, params SearchExample[] examples)
        {
            VisualElement section = AssetInventoryUITK.CreateSection(title);
            section.AddToClassList("ai-example-search-section");

            for (int i = 0; i < examples.Length; i++)
            {
                section.Add(CreateExampleRow(examples[i]));
            }

            return section;
        }

        private VisualElement CreateExampleRow(SearchExample example)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-example-search-row");

            Label label = AssetInventoryUITK.CreateCopyLabel(example.Label);
            label.tooltip = example.SearchPhrase;
            label.AddToClassList("ai-example-search-label");
            row.Add(label);

            Button button = AssetInventoryUITK.CreateSecondaryButton("Use", () => SelectSearch(example.SearchPhrase, example.SearchType));
            button.tooltip = "Use this example in Search.";
            button.AddToClassList("ai-example-search-button");
            row.Add(button);

            return row;
        }

        private VisualElement BuildAISearchSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Describe in English (AI, Experimental)");
            section.AddToClassList("ai-example-search-section");

            if (Intelligence.IsOllamaInstalled)
            {
                VisualElement row = new VisualElement();
                row.AddToClassList("ai-inline-control-row");

                TextField phraseField = new TextField
                {
                    value = _phrase,
                    tooltip = "Describe the files you want to find in plain English."
                };
                phraseField.AddToClassList("ai-inline-grow");
                phraseField.RegisterValueChangedCallback(evt => _phrase = evt.newValue ?? string.Empty);
                phraseField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;

                    CreateAISearch(_phrase);
                    evt.StopPropagation();
                });
                row.Add(phraseField);

                Button set = AssetInventoryUITK.CreateSecondaryButton("Use", () => CreateAISearch(_phrase));
                set.tooltip = "Convert this description into an Asset Inventory query.";
                set.AddToClassList("ai-example-search-button");
                row.Add(set);

                section.Add(row);
            }
            else
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("AI search requires Ollama to be installed and active.", MessageType.Info));
            }

            return section;
        }

        private void SelectSearch(string searchPhrase, string searchType)
        {
            _onSearchSelection?.Invoke(searchPhrase, searchType);
        }

        private async void CreateAISearch(string phrase)
        {
            string systemPrompt = GetSearchSyntaxPrompt();
            string response = await Intelligence.ChatAsync(systemPrompt, phrase);
            SelectSearch(response, null);
        }

        private static string GetSearchSyntaxPrompt()
        {
            string[] guids = AssetDatabase.FindAssets("SearchSyntax t:TextAsset");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith("SearchSyntax.md"))
                {
                    return System.IO.File.ReadAllText(assetPath);
                }
            }

            return "Search syntax documentation not found.";
        }

        private readonly struct SearchExample
        {
            internal SearchExample(string label, string searchPhrase, string searchType = null)
            {
                Label = label;
                SearchPhrase = searchPhrase;
                SearchType = searchType;
            }

            internal string Label { get; }
            internal string SearchPhrase { get; }
            internal string SearchType { get; }
        }
    }
}
