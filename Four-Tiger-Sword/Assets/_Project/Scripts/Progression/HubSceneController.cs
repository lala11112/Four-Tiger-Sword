using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Menu-Hub navigation; the upgrade view remains usable by a later world Hub.</summary>
public sealed class HubSceneController : MonoBehaviour
{
    [SerializeField] private HubUpgradeView upgrades;
    [SerializeField] private UnityEngine.UI.Button startButton, titleButton, retryButton;
    [SerializeField] private TMP_Text status;
    [SerializeField] private BoundaryZeroRunOverlay loadingOverlay;
    private PlayerUpgradeService _service;
    public bool IsLoading { get; private set; }

    public void Configure(HubUpgradeView view, UnityEngine.UI.Button start, UnityEngine.UI.Button title,
        UnityEngine.UI.Button retry, TMP_Text feedback, BoundaryZeroRunOverlay overlay)
    { upgrades = view; startButton = start; titleButton = title; retryButton = retry; status = feedback; loadingOverlay = overlay; }

    private void Start()
    {
        CombatHitStopService.SetExternalWorldTimeScale(1f);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        Bind(PlayerUpgradeService.Instance);
        upgrades.Open();
        startButton.onClick.AddListener(StartRun);
        titleButton.onClick.AddListener(ReturnToTitle);
        retryButton.onClick.AddListener(RetryLoad);
        if (loadingOverlay != null) loadingOverlay.Hide();
        Refresh();
        SelectDefault();
    }

    public void Bind(PlayerUpgradeService service)
    {
        if (service == null) throw new System.ArgumentNullException(nameof(service));
        if (_service != null) _service.Changed -= Refresh;
        _service = service;
        upgrades.Bind(service);
        _service.Changed += Refresh;
        Refresh();
    }

    public void StartRun() { if (!IsLoading && _service != null && _service.IsReady) StartCoroutine(Load("Game Scene")); }
    public void ReturnToTitle() { if (!IsLoading) StartCoroutine(Load("TitleScene")); }
    public void RetryLoad()
    {
        if (IsLoading || _service == null) return;
        if (!_service.TryReload(out string error)) status.text = error;
        else status.text = "강화 데이터를 불러왔습니다.";
        Refresh();
        SelectDefault();
    }

    private void Refresh()
    {
        bool ready = _service != null && _service.IsReady;
        startButton.interactable = ready && !IsLoading;
        titleButton.interactable = !IsLoading;
        retryButton.gameObject.SetActive(!ready);
        retryButton.interactable = !IsLoading;
        if (!ready) status.text = "강화 데이터를 불러오지 못했습니다. 다시 불러오기를 눌러 주세요.";
        upgrades.Refresh();
    }

    private IEnumerator Load(string scene)
    {
        if (!Application.CanStreamedLevelBeLoaded(scene)) { status.text = "이동할 씬을 찾지 못했습니다: " + scene; yield break; }
        IsLoading = true;
        Refresh();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (loadingOverlay != null)
        {
            loadingOverlay.ShowLoading();
            loadingOverlay.Title.text = "이동 중...";
            loadingOverlay.Details.text = string.Empty;
            loadingOverlay.ActionButton.gameObject.SetActive(false);
            loadingOverlay.StageLabel.gameObject.SetActive(false);
            loadingOverlay.ModalRoot.SetActive(true);
        }
        Canvas.ForceUpdateCanvases();
        yield return null;
        AsyncOperation operation = null;
        try { operation = SceneManager.LoadSceneAsync(scene); }
        catch (System.Exception error) { status.text = "씬 이동 실패: " + error.Message; }
        if (operation != null) { while (!operation.isDone) yield return null; yield break; }
        IsLoading = false;
        if (loadingOverlay != null) loadingOverlay.Hide();
        Refresh();
        SelectDefault();
    }

    private void SelectDefault()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(startButton.IsInteractable() ? startButton.gameObject : retryButton.gameObject);
    }

    private void OnDestroy()
    {
        if (_service != null) _service.Changed -= Refresh;
        if (startButton != null) startButton.onClick.RemoveListener(StartRun);
        if (titleButton != null) titleButton.onClick.RemoveListener(ReturnToTitle);
        if (retryButton != null) retryButton.onClick.RemoveListener(RetryLoad);
    }
}
