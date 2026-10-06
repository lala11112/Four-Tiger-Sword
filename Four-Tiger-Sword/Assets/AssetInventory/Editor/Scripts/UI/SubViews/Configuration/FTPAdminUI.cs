using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class FTPAdminUI : BasicEditorUI
    {
        public static FTPAdminUI ShowWindow()
        {
            FTPAdminUI window = GetWindow<FTPAdminUI>("FTP/SFTP Administration");
            window.minSize = new Vector2(660, 360);
            return window;
        }

        private List<FTPConnection> _connections;
        private FTPConnection _selectedConnection;
        private int _selectedIndex = -1;
        private string _tempPassword = string.Empty;
        private bool _showPassword;
        private bool _isEditing;
        private ListView _connectionsList;
        private VisualElement _detailsPanel;
        private Button _saveButton;
        private Button _deleteButton;

        private void OnEnable()
        {
            LoadConnections();
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        private void BuildContent()
        {
            if (_connections == null) LoadConnections();

            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            root.Add(AssetInventoryUITK.CreateHelpBox("Manage FTP and SFTP connections for file upload actions.", MessageType.Info));

            VisualElement split = new VisualElement();
            split.AddToClassList("ai-split-view");

            split.Add(BuildConnectionListPanel());
            _detailsPanel = BuildDetailsPanel();
            split.Add(_detailsPanel);

            root.Add(split);
        }

        private VisualElement BuildConnectionListPanel()
        {
            VisualElement panel = AssetInventoryUITK.CreateSection("Connections");
            panel.AddToClassList("ai-side-panel");

            if (_connections.Count == 0)
            {
                panel.Add(AssetInventoryUITK.CreateEmptyState(
                    "No connections yet",
                    "Add a connection to configure FTP or SFTP uploads.",
                    AssetInventoryUITK.CreatePrimaryButton("Add Connection", AddNewConnection)));
                _connectionsList = null;
                _deleteButton = null;
                return panel;
            }

            _connectionsList = new ListView(
                _connections,
                36f,
                CreateConnectionRow,
                BindConnectionRow)
            {
                fixedItemHeight = 36f,
                horizontalScrollingEnabled = false,
                reorderable = false,
                selectionType = SelectionType.Single,
                showAddRemoveFooter = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = false,
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight
            };
            _connectionsList.AddToClassList("ai-connection-list");
            _connectionsList.selectionChanged += _ => SelectConnection(_connectionsList.selectedIndex);
            panel.Add(_connectionsList);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-inline-control-row");
            actions.AddToClassList("ai-connection-actions");
            actions.Add(AssetInventoryUITK.CreateSecondaryButton("Add", AddNewConnection));
            _deleteButton = AssetInventoryUITK.CreateDestructiveButton("Delete", DeleteConnection);
            _deleteButton.SetEnabled(_selectedConnection != null);
            _deleteButton.tooltip = _selectedConnection == null
                ? "Select a connection to delete it."
                : "Delete the selected connection.";
            actions.Add(_deleteButton);
            panel.Add(actions);

            return panel;
        }

        private static VisualElement CreateConnectionRow()
        {
            VisualElement row = new VisualElement();
            return AssetInventoryUITK.PopulateListRow(
                row,
                string.Empty,
                string.Empty,
                collectionItem: true, extraClasses: new[] {"ai-connection-row"});
        }

        private void BindConnectionRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _connections.Count) return;

            FTPConnection connection = _connections[index];

            string name = string.IsNullOrWhiteSpace(connection.name) ? "<Unnamed>" : connection.name;
            string protocol = connection.protocol == FTPConnection.FTPProtocol.SFTP ? "SFTP" : "FTP";
            string meta = string.IsNullOrWhiteSpace(connection.host) ? protocol : $"{protocol} - {connection.host}";
            CommonUITK.SetTitleSubtitleRowText(element, name, meta);
        }

        private VisualElement BuildDetailsPanel()
        {
            VisualElement panel = AssetInventoryUITK.CreateSection("Connection Details");
            panel.AddToClassList("ai-detail-panel");
            PopulateDetailsPanel(panel);
            return panel;
        }

        private void PopulateDetailsPanel(VisualElement panel)
        {
            panel.Clear();
            Label title = AssetInventoryUITK.CreateCopyLabel("Connection Details");
            title.AddToClassList("ai-section-title");
            panel.Add(title);

            if (_selectedConnection == null || _selectedIndex < 0)
            {
                panel.Add(AssetInventoryUITK.CreateHelpBox(
                    _connections.Count == 0 ? "Add a connection to get started." : "Select a connection from the list.",
                    MessageType.Info));
                return;
            }

            TextField nameField = new TextField
            {
                value = _selectedConnection.name ?? string.Empty,
                tooltip = "A recognizable name used to identify this connection in Asset Inventory."
            };
            nameField.RegisterValueChangedCallback(evt =>
            {
                _selectedConnection.name = evt.newValue;
                MarkEditing();
            });
            panel.Add(AssetInventoryUITK.CreateFieldRow("Connection Name", nameField));

            EnumField protocolField = new EnumField(_selectedConnection.protocol)
            {
                tooltip = "Choose FTP or SFTP. Changing the protocol also resets the port to its default."
            };
            protocolField.RegisterValueChangedCallback(evt =>
            {
                FTPConnection.FTPProtocol oldProtocol = _selectedConnection.protocol;
                _selectedConnection.protocol = (FTPConnection.FTPProtocol)evt.newValue;
                if (oldProtocol != _selectedConnection.protocol)
                {
                    _selectedConnection.port = _selectedConnection.GetDefaultPort();
                    MarkEditing();
                    RefreshDetails();
                    return;
                }
                MarkEditing();
            });
            panel.Add(AssetInventoryUITK.CreateFieldRow("Protocol", protocolField));

            TextField hostField = new TextField
            {
                value = _selectedConnection.host ?? string.Empty,
                tooltip = "Host name or IP address of the FTP or SFTP server."
            };
            hostField.RegisterValueChangedCallback(evt =>
            {
                _selectedConnection.host = evt.newValue;
                MarkEditing();
            });
            panel.Add(AssetInventoryUITK.CreateFieldRow("Host/Server", hostField));

            IntegerField portField = new IntegerField
            {
                value = _selectedConnection.port,
                tooltip = "Server port. FTP normally uses 21 and SFTP normally uses 22."
            };
            portField.RegisterValueChangedCallback(evt =>
            {
                _selectedConnection.port = evt.newValue <= 0 ? _selectedConnection.GetDefaultPort() : evt.newValue;
                MarkEditing();
            });
            panel.Add(AssetInventoryUITK.CreateFieldRow("Port", portField));

            TextField usernameField = new TextField
            {
                value = _selectedConnection.username ?? string.Empty,
                tooltip = "Account name used to sign in to the server."
            };
            usernameField.RegisterValueChangedCallback(evt =>
            {
                _selectedConnection.username = evt.newValue;
                MarkEditing();
            });
            panel.Add(AssetInventoryUITK.CreateFieldRow("Username", usernameField));

            panel.Add(BuildPasswordRow());

            if (!string.IsNullOrEmpty(_tempPassword))
            {
                Label passwordHint = AssetInventoryUITK.CreateCopyLabel("Password will be encrypted when saved.");
                passwordHint.AddToClassList("ai-indented-hint");
                panel.Add(passwordHint);
            }

            if (ShowAdvanced() && _selectedConnection.protocol == FTPConnection.FTPProtocol.FTP)
            {
                Toggle sslToggle = new Toggle
                {
                    value = _selectedConnection.useSsl,
                    tooltip = "Encrypt the FTP connection with SSL/TLS."
                };
                sslToggle.RegisterValueChangedCallback(evt =>
                {
                    _selectedConnection.useSsl = evt.newValue;
                    MarkEditing();
                });
                panel.Add(AssetInventoryUITK.CreateFieldRow("Use SSL/TLS", sslToggle));

                Toggle certificateToggle = new Toggle
                {
                    value = _selectedConnection.validateCertificate,
                    tooltip = "Reject servers whose SSL certificate cannot be validated."
                };
                certificateToggle.RegisterValueChangedCallback(evt =>
                {
                    _selectedConnection.validateCertificate = evt.newValue;
                    MarkEditing();
                });
                panel.Add(AssetInventoryUITK.CreateFieldRow("Validate Certificate", certificateToggle));
            }

            panel.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            VisualElement actions = AssetInventoryUITK.CreateFooter();
            _saveButton = AssetInventoryUITK.CreatePrimaryButton("Save Changes", SaveCurrentConnection);
            _saveButton.SetEnabled(CanSaveEdits());
            _saveButton.tooltip = CanSaveEdits()
                ? "Encrypt the password and save this connection."
                : "Change a setting and enter both a connection name and host before saving.";
            actions.Add(_saveButton);
            actions.Add(AssetInventoryUITK.CreateSecondaryButton("Test Connection", TestConnection));
            panel.Add(actions);
        }

        private VisualElement BuildPasswordRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-inline-control-row");

            TextField passwordField = new TextField
            {
                value = _tempPassword,
                isPasswordField = !_showPassword,
                tooltip = "Password used to sign in. It is encrypted when saved."
            };
            passwordField.AddToClassList("ai-inline-grow");
            passwordField.RegisterValueChangedCallback(evt =>
            {
                _tempPassword = evt.newValue;
                MarkEditing();
            });

            row.Add(passwordField);

            Button showButton = AssetInventoryUITK.CreateSecondaryButton(_showPassword ? "Hide" : "Show", () =>
            {
                _showPassword = !_showPassword;
                RefreshDetails();
            });
            showButton.AddToClassList("ai-small-button");
            row.Add(showButton);

            return AssetInventoryUITK.CreateFieldRow("Password", row);
        }

        private void LoadConnections()
        {
            if (AI.Config.ftpConnections == null)
            {
                AI.Config.ftpConnections = new List<FTPConnection>();
            }
            _connections = AI.Config.ftpConnections;
            SortConnections();
        }

        private void SortConnections()
        {
            _connections.Sort((a, b) =>
            {
                string nameA = string.IsNullOrEmpty(a.name) ? string.Empty : a.name;
                string nameB = string.IsNullOrEmpty(b.name) ? string.Empty : b.name;
                return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void SaveConnections()
        {
            AI.SaveConfig();
            AI.Actions.Init(true); // FIXME: remove when actions support data init callback
        }

        private void SelectConnection(int index)
        {
            if (index < 0 || index >= _connections.Count)
            {
                _selectedIndex = -1;
                _selectedConnection = null;
                _tempPassword = string.Empty;
                _showPassword = false;
                _isEditing = false;
                _deleteButton?.SetEnabled(false);
                RefreshDetails();
                return;
            }

            _selectedIndex = index;
            _selectedConnection = _connections[index].Clone();
            _tempPassword = string.Empty;
            _showPassword = false;
            _isEditing = false;

            if (!string.IsNullOrEmpty(_selectedConnection.encryptedPassword))
            {
                _tempPassword = EncryptionUtil.Decrypt(_selectedConnection.encryptedPassword);
                if (string.IsNullOrEmpty(_tempPassword))
                {
                    _tempPassword = string.Empty;
                    Debug.LogWarning("Could not decrypt password for connection: " + _selectedConnection.name);
                }
            }

            _deleteButton?.SetEnabled(true);
            RefreshDetails();
        }

        private void AddNewConnection()
        {
            FTPConnection newConnection = new FTPConnection
            {
                name = "New Connection",
                port = 21
            };

            _connections.Add(newConnection);
            _selectedIndex = _connections.Count - 1;
            if (_connectionsList == null)
            {
                BuildContent();
            }
            _connectionsList?.RefreshItems();
            _connectionsList?.SetSelection(_selectedIndex);
            SelectConnection(_selectedIndex);
            _isEditing = true;
            UpdateSaveButton();
        }

        private void SaveCurrentConnection()
        {
            if (_selectedConnection == null || _selectedIndex < 0) return;

            if (string.IsNullOrEmpty(_selectedConnection.name))
            {
                EditorUtility.DisplayDialog("Validation Error", "Connection name cannot be empty.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_selectedConnection.host))
            {
                EditorUtility.DisplayDialog("Validation Error", "Host cannot be empty.", "OK");
                return;
            }

            if (!string.IsNullOrEmpty(_tempPassword))
            {
                _selectedConnection.encryptedPassword = EncryptionUtil.Encrypt(_tempPassword);
                if (string.IsNullOrEmpty(_selectedConnection.encryptedPassword))
                {
                    EditorUtility.DisplayDialog("Error", "Failed to encrypt password.", "OK");
                    return;
                }
            }

            _connections[_selectedIndex] = _selectedConnection;

            string savedConnectionId = _selectedConnection.key;
            SortConnections();
            _selectedIndex = _connections.FindIndex(c => c.key == savedConnectionId);

            SaveConnections();
            _isEditing = false;
            _connectionsList?.RefreshItems();
            _connectionsList?.SetSelection(_selectedIndex);
            UpdateSaveButton();
        }

        private void DeleteConnection()
        {
            if (_selectedConnection == null || _selectedIndex < 0) return;

            if (!EditorUtility.DisplayDialog(
                    "Delete Connection",
                    $"Are you sure you want to delete the connection '{_selectedConnection.name}'?",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            _connections.RemoveAt(_selectedIndex);

            _selectedConnection = null;
            _selectedIndex = -1;
            _tempPassword = string.Empty;
            _isEditing = false;
            _showPassword = false;

            SaveConnections();
            _connectionsList?.RefreshItems();
            _connectionsList?.ClearSelection();
            _deleteButton?.SetEnabled(false);
            RefreshDetails();
        }

        private async void TestConnection()
        {
            if (_selectedConnection == null) return;

            if (string.IsNullOrEmpty(_selectedConnection.host))
            {
                EditorUtility.DisplayDialog("Error", "Host cannot be empty.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_selectedConnection.username))
            {
                EditorUtility.DisplayDialog("Error", "Username cannot be empty.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(_tempPassword))
            {
                EditorUtility.DisplayDialog("Error", "Password cannot be empty.", "OK");
                return;
            }

            string protocolName = _selectedConnection.protocol == FTPConnection.FTPProtocol.SFTP ? "SFTP" : "FTP";
            EditorUtility.DisplayProgressBar("Testing Connection", $"Connecting to {protocolName} server at {_selectedConnection.host}...", 0.5f);

            try
            {
                bool success = false;
                string errorMessage = string.Empty;

                if (_selectedConnection.protocol == FTPConnection.FTPProtocol.SFTP)
                {
                    await Task.Run(() => { success = SFTPUtil.TestConnection(_selectedConnection, _tempPassword, out errorMessage); });
                }
                else
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            string uri = $"ftp://{_selectedConnection.host}:{_selectedConnection.port}/";

                            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(uri);
                            request.Method = WebRequestMethods.Ftp.ListDirectory;
                            request.Credentials = new NetworkCredential(_selectedConnection.username, _tempPassword);
                            request.UsePassive = true;
                            request.KeepAlive = false;
                            request.Timeout = 10000;

                            if (_selectedConnection.useSsl)
                            {
                                request.EnableSsl = true;
                                if (!_selectedConnection.validateCertificate)
                                {
                                    ServicePointManager.ServerCertificateValidationCallback = (s, cert, chain, sslPolicyErrors) => true;
                                }
                            }

                            using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                            {
                                if (response.StatusCode == FtpStatusCode.OpeningData ||
                                    response.StatusCode == FtpStatusCode.DataAlreadyOpen ||
                                    response.StatusCode == FtpStatusCode.PathnameCreated)
                                {
                                    success = true;
                                }
                            }

                            ServicePointManager.ServerCertificateValidationCallback = null;
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);

                            errorMessage = e.Message;
                            ServicePointManager.ServerCertificateValidationCallback = null;
                        }
                    });
                }

                EditorUtility.ClearProgressBar();

                if (success)
                {
                    EditorUtility.DisplayDialog(
                        "Connection Successful",
                        $"Successfully connected to {protocolName} server. The connection is ready to use.",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Connection Failed",
                        $"Could not connect to {protocolName} server.\n\n" +
                        $"Error: {errorMessage}\n\n" +
                        "Please check your connection and certificate validation settings and credentials.",
                        "OK");
                }
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Connection Test Error", $"An error occurred while testing the connection:\n\n{e.Message}", "OK");
            }
        }

        private bool CanSaveEdits()
        {
            return _isEditing || !string.IsNullOrEmpty(_tempPassword);
        }

        private void MarkEditing()
        {
            _isEditing = true;
            UpdateSaveButton();
            _connectionsList?.RefreshItems();
        }

        private void UpdateSaveButton()
        {
            bool canSave = CanSaveEdits();
            _saveButton?.SetEnabled(canSave);
            if (_saveButton != null)
            {
                _saveButton.tooltip = canSave
                    ? "Encrypt the password and save this connection."
                    : "Change a setting and enter both a connection name and host before saving.";
            }
        }

        private void RefreshDetails()
        {
            if (_detailsPanel == null) return;

            PopulateDetailsPanel(_detailsPanel);
        }
    }
}
