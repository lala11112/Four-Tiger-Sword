using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Coordinates automatic and manual opening for a product welcome window.
    /// </summary>
    public sealed class CommonWelcomeController<TWindow> where TWindow : EditorWindow
    {
        private const string PreferencePrefix = "Wetzold.Welcome.";

        private readonly string _preferenceKey;
        private readonly Func<TWindow> _openWindow;
        private readonly ICommonWelcomeEnvironment _environment;
        private readonly ICommonWelcomePreferenceStore _preferences;

        private bool _automaticOpenSubscribed;
        private bool _markShownSubscribed;
        private TWindow _windowAwaitingContent;

        public CommonWelcomeController(
            string packageId,
            int revision,
            Func<TWindow> openWindow)
            : this(
                packageId,
                revision,
                openWindow,
                UnityCommonWelcomeEnvironment.Instance,
                EditorPrefsCommonWelcomePreferenceStore.Instance)
        {
        }

        internal CommonWelcomeController(
            string packageId,
            int revision,
            Func<TWindow> openWindow,
            ICommonWelcomeEnvironment environment,
            ICommonWelcomePreferenceStore preferences)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentException("A package ID is required.", nameof(packageId));
            if (revision <= 0)
                throw new ArgumentOutOfRangeException(nameof(revision), "The welcome revision must be positive.");

            _openWindow = openWindow ?? throw new ArgumentNullException(nameof(openWindow));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            _preferenceKey = CreatePreferenceKey(packageId, revision);
        }

        internal string PreferenceKey => _preferenceKey;

        public void ScheduleAutomaticOpen()
        {
            CancelAutomaticOpen();

            if (_markShownSubscribed)
            {
                if (_windowAwaitingContent != null)
                    return;

                CancelPendingMark();
            }

            if (!IsAutomaticOpenAllowed())
                return;
            if (_preferences.GetBool(_preferenceKey, false))
                return;

            _environment.SubscribeUpdate(TryOpenAutomatically);
            _automaticOpenSubscribed = true;
        }

        public TWindow OpenManually()
        {
            CancelAutomaticOpen();
            CancelPendingMark();

            if (_environment.IsBatchMode)
                return null;

            bool markShown = IsStandaloneInstallationPath(
                _environment.ResolveScriptAssetPath(typeof(TWindow)));
            return OpenWindow(markShown);
        }

        internal static bool IsAutomaticOpenAllowed(bool isBatchMode, string scriptAssetPath)
        {
            return !isBatchMode && IsStandaloneInstallationPath(scriptAssetPath);
        }

        internal static bool IsStandaloneInstallationPath(string scriptAssetPath)
        {
            if (string.IsNullOrWhiteSpace(scriptAssetPath))
                return false;

            string normalizedPath = scriptAssetPath.Replace('\\', '/');
            string[] segments = normalizedPath.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], "Reuse", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        internal static string CreatePreferenceKey(string packageId, int revision)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentException("A package ID is required.", nameof(packageId));
            if (revision <= 0)
                throw new ArgumentOutOfRangeException(nameof(revision), "The welcome revision must be positive.");

            return PreferencePrefix + packageId.Trim() + ".v" + revision.ToString(CultureInfo.InvariantCulture);
        }

        private bool IsAutomaticOpenAllowed()
        {
            string scriptAssetPath = _environment.ResolveScriptAssetPath(typeof(TWindow));
            return IsAutomaticOpenAllowed(_environment.IsBatchMode, scriptAssetPath);
        }

        private void TryOpenAutomatically()
        {
            if (!_environment.IsEditorReady)
                return;

            if (!IsAutomaticOpenAllowed() || _preferences.GetBool(_preferenceKey, false))
            {
                CancelAutomaticOpen();
                return;
            }

            CancelAutomaticOpen();
            OpenWindow(true);
        }

        private TWindow OpenWindow(bool markShown)
        {
            TWindow window;
            try
            {
                window = _openWindow();
            }
            catch (Exception exception)
            {
                _environment.LogException(exception);
                return null;
            }

            if (window != null && markShown)
                BeginMarkWhenReady(window);

            return window;
        }

        private void BeginMarkWhenReady(TWindow window)
        {
            CancelPendingMark();
            _windowAwaitingContent = window;
            if (HasBuiltContent(window))
            {
                MarkShown();
                return;
            }

            _environment.SubscribeUpdate(TryMarkShown);
            _markShownSubscribed = true;
        }

        private void TryMarkShown()
        {
            if (_windowAwaitingContent == null)
            {
                CancelPendingMark();
                return;
            }

            if (HasBuiltContent(_windowAwaitingContent))
                MarkShown();
        }

        private void MarkShown()
        {
            _preferences.SetBool(_preferenceKey, true);
            CancelPendingMark();
        }

        private static bool HasBuiltContent(TWindow window)
        {
            return window != null &&
                   window.rootVisualElement != null &&
                   window.rootVisualElement.childCount > 0;
        }

        private void CancelAutomaticOpen()
        {
            if (!_automaticOpenSubscribed)
                return;

            _environment.UnsubscribeUpdate(TryOpenAutomatically);
            _automaticOpenSubscribed = false;
        }

        private void CancelPendingMark()
        {
            if (_markShownSubscribed)
            {
                _environment.UnsubscribeUpdate(TryMarkShown);
                _markShownSubscribed = false;
            }

            _windowAwaitingContent = null;
        }
    }

    internal interface ICommonWelcomeEnvironment
    {
        bool IsBatchMode { get; }
        bool IsEditorReady { get; }
        string ResolveScriptAssetPath(Type windowType);
        void SubscribeUpdate(EditorApplication.CallbackFunction callback);
        void UnsubscribeUpdate(EditorApplication.CallbackFunction callback);
        void LogException(Exception exception);
    }

    internal interface ICommonWelcomePreferenceStore
    {
        bool GetBool(string key, bool defaultValue);
        void SetBool(string key, bool value);
    }

#if UNITY_6000_7_OR_NEWER
    // The environment adapter is a stateless code-lifetime singleton.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal sealed partial class UnityCommonWelcomeEnvironment : ICommonWelcomeEnvironment
    {
        internal static readonly UnityCommonWelcomeEnvironment Instance = new UnityCommonWelcomeEnvironment();

        private UnityCommonWelcomeEnvironment()
        {
        }

        public bool IsBatchMode => Application.isBatchMode;

        public bool IsEditorReady =>
            !EditorApplication.isCompiling &&
            !EditorApplication.isUpdating &&
            !EditorApplication.isPlayingOrWillChangePlaymode;

        public string ResolveScriptAssetPath(Type windowType)
        {
            if (windowType == null)
                return null;

            string[] guids = AssetDatabase.FindAssets(windowType.Name + " t:MonoScript");
            for (int i = 0; i < guids.Length; i++)
            {
                string scriptAssetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptAssetPath);
                if (script != null && script.GetClass() == windowType)
                    return scriptAssetPath;
            }

            return null;
        }

        public void SubscribeUpdate(EditorApplication.CallbackFunction callback)
        {
            EditorApplication.update -= callback;
            EditorApplication.update += callback;
        }

        public void UnsubscribeUpdate(EditorApplication.CallbackFunction callback)
        {
            EditorApplication.update -= callback;
        }

        public void LogException(Exception exception)
        {
            Debug.LogException(exception);
        }
    }

#if UNITY_6000_7_OR_NEWER
    // The preference adapter is a stateless code-lifetime singleton.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal sealed partial class EditorPrefsCommonWelcomePreferenceStore : ICommonWelcomePreferenceStore
    {
        internal static readonly EditorPrefsCommonWelcomePreferenceStore Instance =
            new EditorPrefsCommonWelcomePreferenceStore();

        private EditorPrefsCommonWelcomePreferenceStore()
        {
        }

        public bool GetBool(string key, bool defaultValue)
        {
            return EditorPrefs.GetBool(key, defaultValue);
        }

        public void SetBool(string key, bool value)
        {
            EditorPrefs.SetBool(key, value);
        }
    }
}
