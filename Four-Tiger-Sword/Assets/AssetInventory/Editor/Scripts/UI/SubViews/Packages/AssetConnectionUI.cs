using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class AssetConnectionUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(500f, 260f);

        private string _url = string.Empty;
        private Action<AssetDetails> _callback;
        private bool _invalidInput;
        private bool _isResolving;
        private AssetDetails _resolvedAsset;
        private TextField _urlField;
        private Button _verifyButton;
        private VisualElement _feedbackContainer;

        public static AssetConnectionUI ShowDropdown(Rect anchor, Action<AssetDetails> callback)
        {
            AssetConnectionUI window = CreateInstance<AssetConnectionUI>();
            window.titleContent = new GUIContent("Connect Asset Store Metadata");
            window.minSize = WindowSize;
            window.Init(callback);

            AssetInventoryUITK.ShowAsDropDown(window, anchor, WindowSize);
            return window;
        }

        public static AssetConnectionUI ShowWindow(Action<AssetDetails> callback = null)
        {
            AssetConnectionUI window = GetWindow<AssetConnectionUI>("Connect Asset Store Metadata");
            window.minSize = WindowSize;
            window.Init(callback);
            return window;
        }

        public void Init(Action<AssetDetails> callback)
        {
            _callback = callback;
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

            VisualElement section = AssetInventoryUITK.CreateSection("Connect Free-Floating Asset");
            section.Add(AssetInventoryUITK.CreateCopyLabel("Link this package to Asset Store metadata by entering the full Asset Store URL or numeric package id."));

            VisualElement urlRow = new VisualElement();
            urlRow.AddToClassList("ai-inline-control-row");

            _urlField = new TextField
            {
                name = "url-field",
                value = _url
            };
            _urlField.AddToClassList("ai-inline-grow");
            _urlField.RegisterValueChangedCallback(evt =>
            {
                _url = evt.newValue ?? string.Empty;
                _invalidInput = false;
                _resolvedAsset = null;
                RefreshState();
            });
            _urlField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;

                CheckURL(_url);
                evt.StopPropagation();
            });
            urlRow.Add(_urlField);

            _verifyButton = AssetInventoryUITK.CreatePrimaryButton(_isResolving ? "Verifying..." : "Verify", () => CheckURL(_url));
            _verifyButton.AddToClassList("ai-asset-connect-verify-button");
            _verifyButton.SetEnabled(!_isResolving && !string.IsNullOrWhiteSpace(_url));
            urlRow.Add(_verifyButton);
            section.Add(AssetInventoryUITK.CreateFieldRow("Url/Id", urlRow));

            _feedbackContainer = new VisualElement();
            section.Add(_feedbackContainer);
            PopulateFeedback(_feedbackContainer);

            root.Add(section);
            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            root.schedule.Execute(() => _urlField?.Focus()).ExecuteLater(100);
        }

        private void PopulateFeedback(VisualElement container)
        {
            if (container == null) return;

            container.Clear();

            if (string.IsNullOrWhiteSpace(_url))
            {
                container.Add(AssetInventoryUITK.CreateHelpBox(
                    $"Use the full Asset Store URL, not a short URL. You can also enter the package id directly, e.g. {AI.ASSET_STORE_ID}.",
                    MessageType.Info));

                Button link = AssetInventoryUITK.CreateButton($"https://assetstore.unity.com/packages/tools/utilities/asset-inventory-{AI.ASSET_STORE_ID}",
                    () => AI.OpenStoreURL($"https://assetstore.unity.com/packages/tools/utilities/asset-inventory-{AI.ASSET_STORE_ID}"));
                link.AddToClassList("ai-link-button");
                container.Add(link);
            }
            else if (_invalidInput)
            {
                container.Add(AssetInventoryUITK.CreateHelpBox("The entered URL could not be resolved correctly.", MessageType.Error));
            }
            else if (_isResolving)
            {
                container.Add(AssetInventoryUITK.CreateHelpBox("Resolving Asset Store metadata...", MessageType.Info));
            }
            else if (_resolvedAsset != null)
            {
                VisualElement resolved = AssetInventoryUITK.CreateSection("Resolved Asset");
                resolved.AddToClassList("ai-nested-section");
                resolved.Add(AssetInventoryUITK.CreateKeyValueRow("Name", $"{_resolvedAsset.displayName} - {_resolvedAsset.version}"));
                resolved.Add(AssetInventoryUITK.CreateKeyValueRow("Publisher", $"{_resolvedAsset.productPublisher.name}, {_resolvedAsset.state}"));
                container.Add(resolved);

                VisualElement footer = AssetInventoryUITK.CreateFooter();
                footer.Add(AssetInventoryUITK.CreatePrimaryButton("Connect", ConnectResolvedAsset));
                container.Add(footer);
            }
        }

        private void RefreshState()
        {
            _verifyButton?.SetEnabled(!_isResolving && !string.IsNullOrWhiteSpace(_url));
            if (_verifyButton != null)
            {
                _verifyButton.text = _isResolving ? "Verifying..." : "Verify";
            }
            PopulateFeedback(_feedbackContainer);
        }

        private void ConnectResolvedAsset()
        {
            if (_resolvedAsset == null) return;

            _callback?.Invoke(_resolvedAsset);
            Close();
        }

        private async void CheckURL(string url)
        {
            _invalidInput = false;
            _resolvedAsset = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                RefreshState();
                return;
            }

            _isResolving = true;
            RefreshState();

            string idPart = url.Split('-').Last();
            if (int.TryParse(idPart, out int id))
            {
                _resolvedAsset = await AssetStore.RetrieveAssetDetails(id);
            }

            _isResolving = false;
            _invalidInput = _resolvedAsset == null;
            RefreshState();
        }
    }
}
