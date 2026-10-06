using System;
using System.Collections.Generic;
using Database;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class DatabaseConfigurationUI : BasicEditorUI
    {
        private string _selectedDatabaseType = DatabaseFactory.SQLITE;
        private bool _isTesting;

        // MySQL settings
        private string _mysqlHost = "localhost";
        private int _mysqlPort = 3306;
        private string _mysqlDatabase = "";
        private string _mysqlUser = "";
        private string _mysqlPassword = "";
        private bool _mysqlUseSSL;
        private int _mysqlConnectionTimeout = 30;
        private bool _showPassword;
        private bool _hasChanges;
        private Button _saveButton;
        private Button _testButton;

        // Foldout states for database panels
        private bool _sqlitePanelExpanded;
        private bool _mysqlPanelExpanded;

        public static DatabaseConfigurationUI ShowWindow()
        {
            DatabaseConfigurationUI window = GetWindow<DatabaseConfigurationUI>("Database Configuration");
            window.minSize = new Vector2(900, 350);
            window.Show();

            return window;
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void LoadSettings()
        {
            _selectedDatabaseType = AI.Config.databaseType ?? DatabaseFactory.SQLITE;

            _mysqlHost = AI.Config.mysqlHost ?? "localhost";
            _mysqlPort = AI.Config.mysqlPort > 0 ? AI.Config.mysqlPort : 3306;
            _mysqlDatabase = AI.Config.mysqlDatabase ?? "";
            _mysqlUser = AI.Config.mysqlUser ?? "";
            _mysqlPassword = "";
            if (!string.IsNullOrEmpty(AI.Config.mysqlEncryptedPassword))
            {
                _mysqlPassword = EncryptionUtil.Decrypt(AI.Config.mysqlEncryptedPassword) ?? "";
            }
            _mysqlUseSSL = AI.Config.mysqlUseSSL;
            _mysqlConnectionTimeout = AI.Config.mysqlConnectionTimeout > 0 ? AI.Config.mysqlConnectionTimeout : 30;

            _hasChanges = false;
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            root.Add(CreateCurrentStatus());

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;

            VisualElement selection = AssetInventoryUITK.CreateSection("Select Database Type");
            VisualElement grid = new VisualElement();
            grid.AddToClassList("ai-card-grid");
            grid.Add(CreateDatabasePanel(DatabaseFactory.SQLITE,
                new[]
                {
                    "No server setup required",
                    "Zero configuration",
                    "File-based (easy backup and portability)",
                    "Fast for single-user scenarios",
                    "Embedded, no external dependencies",
                    "Portable across systems"
                },
                new[]
                {
                    "Limited concurrent access",
                    "Limited network access (file share)",
                    "Smaller data amounts (1-2 Gb)",
                    "Limited concurrency"
                },
                "Local development, single-user scenarios, simple deployments",
                _selectedDatabaseType == DatabaseFactory.SQLITE,
                ref _sqlitePanelExpanded));
            grid.Add(CreateDatabasePanel(DatabaseFactory.MYSQL,
                new[]
                {
                    "Multi-user support and concurrent access",
                    "Network accessible",
                    "Highly scalable",
                    "Better for large datasets",
                    "Advanced features and optimizations",
                    "Industry-standard for production"
                },
                new[]
                {
                    "Requires server setup and configuration",
                    "Network dependency",
                    "More complex setup",
                    "Licensing considerations for commercial use"
                },
                "Team environments, remote access, large-scale deployments",
                _selectedDatabaseType == DatabaseFactory.MYSQL,
                ref _mysqlPanelExpanded));
            selection.Add(grid);
            scroll.Add(selection);

            if (_selectedDatabaseType == DatabaseFactory.SQLITE)
            {
                scroll.Add(CreateSQLiteConfiguration());
            }
            else
            {
                scroll.Add(CreateMySQLConfiguration());
            }

            root.Add(scroll);

            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            _saveButton = AssetInventoryUITK.CreatePrimaryButton("Save & Connect", SaveAndConnect);
            footer.Add(_saveButton);

            if (_selectedDatabaseType == DatabaseFactory.MYSQL)
            {
                _testButton = AssetInventoryUITK.CreateSecondaryButton(_isTesting ? "Testing..." : "Test Connection", TestMySQLConnection);
                footer.Add(_testButton);
            }
            else
            {
                _testButton = null;
            }

            UpdateActionButtonStates();
            root.Add(footer);
        }

        private VisualElement CreateCurrentStatus()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Current Database");
            string currentType = AI.Config?.databaseType ?? DatabaseFactory.SQLITE;
            string status = DBAdapter.IsDBOpen() ? "Connected" : "Disconnected";

            section.Add(AssetInventoryUITK.CreateKeyValueRow("Type", currentType));
            string pillClass = DBAdapter.IsDBOpen() ? null : "ai-status-muted";
            section.Add(AssetInventoryUITK.CreateFieldRow("Status", AssetInventoryUITK.CreateStatusPill(status, pillClass)));

            if (!string.IsNullOrEmpty(DBAdapter.DBError))
            {
                section.Add(AssetInventoryUITK.CreateHelpBox($"Connection Error: {DBAdapter.DBError}", MessageType.Error));
            }

            return section;
        }

        private VisualElement CreateDatabasePanel(string name, string[] pros, string[] cons, string bestFor, bool isSelected, ref bool showDetails)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("ai-choice-card");
            card.EnableInClassList("ai-choice-card-active", isSelected);

            Label title = new Label(name);
            title.AddToClassList("ai-choice-card-title");
            card.Add(title);

            Label copy = new Label(bestFor);
            copy.AddToClassList("ai-choice-card-copy");
            card.Add(copy);

            Foldout details = AssetInventoryUITK.CreateFoldout("Compare Trade-offs", showDetails, value =>
            {
                if (name == DatabaseFactory.SQLITE)
                {
                    _sqlitePanelExpanded = value;
                }
                else
                {
                    _mysqlPanelExpanded = value;
                }
            }, $"Compare the advantages and limitations of {name}.");

            details.Add(CreateBulletGroup("Advantages", pros));
            details.Add(CreateBulletGroup("Limitations", cons));
            card.Add(details);

            card.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            VisualElement footer = new VisualElement();
            footer.AddToClassList("ai-choice-card-footer");
            if (isSelected)
            {
                footer.Add(AssetInventoryUITK.CreateStatusPill("Current Selection"));
            }
            else
            {
                footer.Add(AssetInventoryUITK.CreateSecondaryButton("Select", () =>
                {
                    _selectedDatabaseType = name;
                    _hasChanges = true;
                    Build();
                }));
            }

            card.Add(footer);
            return card;
        }

        private static VisualElement CreateBulletGroup(string title, IEnumerable<string> lines)
        {
            VisualElement group = new VisualElement();
            group.AddToClassList("ai-option-section");
            group.Add(AssetInventoryUITK.CreateStatusPill(title, "ai-status-muted"));
            foreach (string line in lines)
            {
                Label label = new Label("- " + line);
                label.AddToClassList("ai-choice-card-copy");
                group.Add(label);
            }

            return group;
        }

        private static VisualElement CreateSQLiteConfiguration()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("SQLite Configuration");
            section.Add(AssetInventoryUITK.CreateHelpBox("SQLite databases are stored as files. Use the database location settings to change the folder.", MessageType.Info));
            return section;
        }

        private VisualElement CreateMySQLConfiguration()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("MySQL Configuration");

            TextField host = CreateTrackedTextField(_mysqlHost, value => _mysqlHost = value);
            host.tooltip = "Host name or IP address of the MySQL server.";
            section.Add(AssetInventoryUITK.CreateFieldRow("Host", host));

            IntegerField port = new IntegerField
            {
                value = _mysqlPort,
                tooltip = "TCP port used by the MySQL server. The default is 3306."
            };
            port.RegisterValueChangedCallback(evt =>
            {
                _mysqlPort = evt.newValue > 0 ? evt.newValue : 3306;
                _hasChanges = true;
                RefreshFooterOnly();
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Port", port));

            TextField database = CreateTrackedTextField(_mysqlDatabase, value => _mysqlDatabase = value);
            database.tooltip = "Name of the database Asset Inventory should use.";
            section.Add(AssetInventoryUITK.CreateFieldRow("Database Name", database));

            TextField username = CreateTrackedTextField(_mysqlUser, value => _mysqlUser = value);
            username.tooltip = "MySQL account used to access the database.";
            section.Add(AssetInventoryUITK.CreateFieldRow("Username", username));

            VisualElement passwordRow = new VisualElement();
            passwordRow.AddToClassList("ai-inline-control-row");
            TextField password = CreateTrackedTextField(_mysqlPassword, value => _mysqlPassword = value);
            password.isPasswordField = !_showPassword;
            password.tooltip = "Password for the MySQL account. It is encrypted when saved.";
            password.AddToClassList("ai-inline-grow");
            passwordRow.Add(password);
            passwordRow.Add(AssetInventoryUITK.CreateSecondaryButton(_showPassword ? "Hide" : "Show", () =>
            {
                _showPassword = !_showPassword;
                Build();
            }));
            section.Add(AssetInventoryUITK.CreateFieldRow("Password", passwordRow));

            if (!string.IsNullOrEmpty(_mysqlPassword))
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("Password will be encrypted when saved.", MessageType.Info));
            }

            if (ShowAdvanced())
            {
                Toggle ssl = new Toggle
                {
                    value = _mysqlUseSSL,
                    tooltip = "Encrypt the connection to the MySQL server with SSL."
                };
                ssl.RegisterValueChangedCallback(evt =>
                {
                    _mysqlUseSSL = evt.newValue;
                    _hasChanges = true;
                    RefreshFooterOnly();
                });
                section.Add(AssetInventoryUITK.CreateFieldRow("Use SSL", ssl));

                IntegerField timeout = new IntegerField
                {
                    value = _mysqlConnectionTimeout,
                    tooltip = "Maximum time to wait for the database connection before reporting an error."
                };
                timeout.RegisterValueChangedCallback(evt =>
                {
                    _mysqlConnectionTimeout = evt.newValue > 0 ? evt.newValue : 30;
                    _hasChanges = true;
                    RefreshFooterOnly();
                });
                section.Add(AssetInventoryUITK.CreateFieldRow("Connection Timeout (s)", timeout));
            }

            return section;
        }

        private TextField CreateTrackedTextField(string value, Action<string> setter)
        {
            TextField field = new TextField
            {
                value = value ?? string.Empty
            };
            field.RegisterValueChangedCallback(evt =>
            {
                setter(evt.newValue);
                _hasChanges = true;
                RefreshFooterOnly();
            });
            return field;
        }

        private void RefreshFooterOnly()
        {
            UpdateActionButtonStates();
        }

        private void UpdateActionButtonStates()
        {
            bool mysqlConfigurationValid = IsMySQLConfigValid();
            bool canSave = _hasChanges && !_isTesting && (_selectedDatabaseType != DatabaseFactory.MYSQL || mysqlConfigurationValid);
            _saveButton?.SetEnabled(canSave);
            if (_saveButton != null)
            {
                _saveButton.tooltip = canSave
                    ? "Save these settings and connect to the selected database."
                    : _isTesting
                        ? "Wait for the connection test to finish."
                        : !_hasChanges
                            ? "Change a database setting before saving."
                            : "Enter a host, database name, and username before connecting.";
            }

            bool canTest = !_isTesting && mysqlConfigurationValid;
            _testButton?.SetEnabled(canTest);
            if (_testButton != null)
            {
                _testButton.tooltip = canTest
                    ? "Test these MySQL settings without saving them."
                    : _isTesting
                        ? "The connection test is running."
                        : "Enter a host, database name, and username before testing.";
            }
        }

        private bool IsMySQLConfigValid()
        {
            return !string.IsNullOrWhiteSpace(_mysqlHost) &&
                !string.IsNullOrWhiteSpace(_mysqlDatabase) &&
                !string.IsNullOrWhiteSpace(_mysqlUser);
        }

        private void TestMySQLConnection()
        {
            _isTesting = true;
            Build();

            // Use EditorApplication.delayCall to allow UI to update
            EditorApplication.delayCall += () =>
            {
                try
                {
                    // Create test settings for MySQL connection
                    DatabaseSettings testSettings = new DatabaseSettings
                    {
                        DatabaseType = DatabaseFactory.MYSQL,
                        MySqlHost = _mysqlHost,
                        MySqlPort = _mysqlPort,
                        MySqlDatabase = _mysqlDatabase,
                        MySqlUser = _mysqlUser,
                        MySqlPassword = _mysqlPassword,
                        MySqlUseSSL = _mysqlUseSSL,
                        MySqlConnectionTimeout = _mysqlConnectionTimeout
                    };

                    TestConnection(new MySQLDatabaseConnection(testSettings), (success, message) =>
                        EditorUtility.DisplayDialog(success ? "Connection Test" : "Connection Test Failed", message, "OK"));
                }
                catch (Exception e)
                {
                    // Log full stack trace to console for debugging
                    Debug.LogError($"MySQL Connection Test Failed:\n{e}");

                    EditorUtility.DisplayDialog("Connection Test Failed",
                        $"Could not connect to MySQL database: {e.Message}",
                        "OK");
                }
                finally
                {
                    _isTesting = false;
                    Build();
                }
            };
        }

        internal static void TestConnection(IDatabaseConnection connection, Action<bool, string> showResult)
        {
            bool success;
            string message;
            try
            {
                using (connection)
                {
                    success = connection.TestConnection();
                }
                message = success
                    ? "Connection successful!"
                    : "Could not connect to the MySQL database. Check the server settings, credentials, and SSL configuration. See the Unity Console for the full error.";
            }
            catch (Exception e)
            {
                Debug.LogError($"MySQL Connection Test Failed:\n{e}");
                success = false;
                message = $"Could not connect to the MySQL database: {e.Message}\n\nSee the Unity Console for the full error.";
            }

            showResult(success, message);
        }

        private void SaveAndConnect()
        {
            try
            {
                // Save configuration
                AI.Config.databaseType = _selectedDatabaseType;

                if (_selectedDatabaseType == DatabaseFactory.MYSQL)
                {
                    AI.Config.mysqlHost = _mysqlHost;
                    AI.Config.mysqlPort = _mysqlPort;
                    AI.Config.mysqlDatabase = _mysqlDatabase;
                    AI.Config.mysqlUser = _mysqlUser;
                    AI.Config.mysqlUseSSL = _mysqlUseSSL;
                    AI.Config.mysqlConnectionTimeout = _mysqlConnectionTimeout;

                    // Encrypt password
                    if (!string.IsNullOrEmpty(_mysqlPassword))
                    {
                        AI.Config.mysqlEncryptedPassword = EncryptionUtil.Encrypt(_mysqlPassword);
                        if (string.IsNullOrEmpty(AI.Config.mysqlEncryptedPassword))
                        {
                            EditorUtility.DisplayDialog("Error", "Failed to encrypt password.", "OK");
                            return;
                        }
                    }
                    else
                    {
                        AI.Config.mysqlEncryptedPassword = "";
                    }
                }

                AI.SaveConfig();

                // Close current connection and switch with full reinitialization
                DBAdapter.Close();
                AI.ClearAllCaches();
                AI.Init(false, true);

                bool connected = CompleteDatabaseSwitch(_selectedDatabaseType, (success, message) =>
                    EditorUtility.DisplayDialog(success ? "Success" : "Connection Error", message, "OK"));
                if (connected)
                {
                    Close();
                }
                else
                {
                    Build();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving database configuration:\n{e}");
                EditorUtility.DisplayDialog("Error",
                    $"Error saving database configuration:\n\n{e.Message}",
                    "OK");
            }
        }

        internal static bool CompleteDatabaseSwitch(string databaseType, Action<bool, string> showResult)
        {
            if (!DBAdapter.IsDBOpen() || !string.IsNullOrEmpty(DBAdapter.DBError))
            {
                string error = string.IsNullOrEmpty(DBAdapter.DBError) ? "The database connection is not open." : DBAdapter.DBError;
                showResult(false, $"Settings were saved, but the connection to the {databaseType} database failed:\n\n{error}\n\nCheck your settings and try again. See the Unity Console for the full error.");
                return false;
            }

            AI.TriggerDatabaseSwitched();
            showResult(true, $"Successfully switched to {databaseType} database.");
            return true;
        }
    }
}
