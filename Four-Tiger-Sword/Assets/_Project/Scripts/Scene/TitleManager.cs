using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    private const string GameSceneName = "Hub Scene";

    [SerializeField] private UnityEngine.UI.Button startButton;
    [SerializeField] private UnityEngine.UI.Button quitButton;
    [SerializeField] private BoundaryZeroRunOverlay loadingOverlay;

    public bool IsLoading { get; private set; }
    private bool _entering;
    private UIModalMotion[] _menuMotions;
    private AsyncOperation _sceneLoad;

    public void Configure(UnityEngine.UI.Button start, UnityEngine.UI.Button quit, BoundaryZeroRunOverlay overlay)
    {
        startButton = start;
        quitButton = quit;
        loadingOverlay = overlay;
    }

    private void Awake()
    {
        CombatHitStopService.SetExternalWorldTimeScale(1f);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        if (loadingOverlay != null) loadingOverlay.Hide();
    }

    private IEnumerator Start()
    {
        if (startButton == null) yield break;
        _entering = true;
        var buttons = startButton.transform.parent.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        _menuMotions = new UIModalMotion[buttons.Length];
        UIButtonMotion.Install(startButton.transform.parent.gameObject);
        SetMenuInteractable(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        for (int i = 0; i < buttons.Length; i++)
        {
            _menuMotions[i] = UIModalMotion.Ensure(buttons[i].gameObject,
                buttons[i].transform as RectTransform, .24f, .16f, 14f);
            _menuMotions[i]?.HideImmediate();
        }
        foreach (var motion in _menuMotions)
        {
            motion?.PlayShow();
            yield return new WaitForSecondsRealtime(.08f);
        }
        while (System.Array.Exists(_menuMotions, m => m != null && m.IsAnimating)) yield return null;
        _entering = false;
        SetMenuInteractable(true);
        SelectStartButton();
    }

    public void OnStartButton()
    {
        if (_entering || IsLoading || !isActiveAndEnabled) return;
        IsLoading = true;
        SetMenuInteractable(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        StartCoroutine(LoadGame());
    }

    public void OnQuitButton()
    {
        if (_entering || IsLoading) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator LoadGame()
    {
        if (!Application.CanStreamedLevelBeLoaded(GameSceneName))
        {
            RestoreMenu("The game scene is not enabled in the build scene list: " + GameSceneName);
            yield break;
        }

        if (loadingOverlay != null)
        {
            loadingOverlay.ShowLoading();
            loadingOverlay.Title.text = "거점으로 이동 중";
            loadingOverlay.Details.text = "영구 강화를 확인하고 다음 탐사를 준비합니다.";
            loadingOverlay.StageLabel.gameObject.SetActive(false);
            loadingOverlay.ActionButton.gameObject.SetActive(false);
            loadingOverlay.ModalRoot.SetActive(true);
        }
        Canvas.ForceUpdateCanvases();
        // Let the loading presentation render before scene deserialization starts.
        yield return null;

        AsyncOperation operation = null;
        string failure = null;
        try { operation = SceneManager.LoadSceneAsync(GameSceneName, LoadSceneMode.Single); }
        catch (System.Exception error) { failure = error.Message; }
        if (operation == null)
        {
            RestoreMenu("Could not load the game scene: " + (failure ?? GameSceneName));
            yield break;
        }
        _sceneLoad = operation;
        while (!operation.isDone) yield return null;
    }

    private void RestoreMenu(string reason)
    {
        if (loadingOverlay != null) loadingOverlay.Hide();
        IsLoading = false;
        SetMenuInteractable(true);
        SelectStartButton();
        Debug.LogWarning(reason, this);
    }

    private void SetMenuInteractable(bool interactable)
    {
        if (startButton != null) startButton.interactable = interactable;
        if (quitButton != null) quitButton.interactable = interactable;
    }

    private void SelectStartButton()
    {
        if (EventSystem.current != null && startButton != null && startButton.IsActive() && startButton.IsInteractable())
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (_menuMotions != null) foreach (var motion in _menuMotions) motion?.ShowImmediate();
        _entering = false;
        IsLoading = _sceneLoad != null && !_sceneLoad.isDone;
        if (!IsLoading && loadingOverlay != null) loadingOverlay.Hide();
        SetMenuInteractable(!IsLoading);
    }

    private void OnEnable()
    {
        // Start runs only once. A re-enabled menu must not retain its entrance input lock.
        if (_menuMotions == null) return;
        IsLoading = _sceneLoad != null && !_sceneLoad.isDone;
        SetMenuInteractable(!IsLoading);
        if (!IsLoading) SelectStartButton();
    }
}
