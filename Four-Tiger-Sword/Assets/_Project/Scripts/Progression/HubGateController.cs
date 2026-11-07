using System;
using System.Collections;
using EasternLab.GateAddon;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>Controls the Hub gate panel and hands a completed walking traversal to the run scene.</summary>
[DisallowMultipleComponent]
public sealed class HubGateController : MonoBehaviour
{
    private const string GameSceneName = "Game Scene";
    [SerializeField] private PlayerController player;
    [SerializeField] private GateAddonSettings settings;
    [SerializeField] private HubWorldController world;
    [SerializeField] private HubWorkshopInteraction workshop;
    [SerializeField] private BoundaryZeroRunOverlay loadingOverlay;
    [SerializeField] private GameObject prompt;
    [SerializeField] private TMP_Text actionLabel, statusLabel;
    [SerializeField, Min(.1f)] private float interactionRadius = 2f;
    private GateTransit _subscribedTransit;
    private Coroutine _loading;
    private CanvasGroup _keycap;
    private Vector3 _safePosition;
    private Quaternion _safeRotation;
    private bool _hasSafePose, _ownsTransition, _sceneLoadCommitted;
    internal Func<string, AsyncOperation> SceneLoaderOverride;
    public bool IsLoading { get; private set; }
    public string LastError { get; private set; }
    public GateAddonSettings Settings => settings;
    private GateTransit Transit => settings != null ? settings.transit : null;
    private GateActivation Activation => Transit != null ? Transit.activation : null;
    private bool PlayerAvailable => player != null && player.isActiveAndEnabled && !player.IsStageTransitioning
        && player.StatManager != null && player.StatManager.CurrentHp > 0f;
    private bool CanControl => isActiveAndEnabled && PlayerAvailable && !IsLoading
        && !player.IsGameplayModalPaused && (world == null || !world.IsModalOpen)
        && (workshop == null || !workshop.IsOpen);

    public bool IsInRange
    {
        get
        {
            if (!PlayerAvailable || settings == null || settings.panel == null || !settings.panel.isActiveAndEnabled) return false;
            Vector3 delta = player.transform.position - settings.panel.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(settings.panel.transform.forward, Vector3.up).normalized;
            return Mathf.Abs(delta.y) <= 2f && new Vector2(delta.x, delta.z).sqrMagnitude <= interactionRadius * interactionRadius
                && Vector3.Dot(Vector3.ProjectOnPlane(delta, Vector3.up), forward) >= -.01f;
        }
    }

    public void Configure(PlayerController targetPlayer, GateAddonSettings gateSettings, HubWorldController worldController,
        HubWorkshopInteraction workshopInteraction, BoundaryZeroRunOverlay overlay, GameObject interactionPrompt,
        TMP_Text interactionAction, TMP_Text interactionStatus, float radius = 2f)
    {
        Unsubscribe();
        player = targetPlayer; settings = gateSettings; world = worldController; workshop = workshopInteraction;
        loadingOverlay = overlay; prompt = interactionPrompt; actionLabel = interactionAction; statusLabel = interactionStatus;
        interactionRadius = Mathf.Max(.1f, radius);
        _hasSafePose = false;
        if (Application.isPlaying && isActiveAndEnabled) Subscribe();
    }

    private void OnEnable() => Subscribe();
    private void Start() { CaptureSafePose(); RefreshPrompt(); }

    private void Subscribe()
    {
        if (_subscribedTransit == Transit) return;
        Unsubscribe();
        _subscribedTransit = Transit;
        if (_subscribedTransit != null) _subscribedTransit.onTraverse.AddListener(OnTraverse);
    }

    private void Unsubscribe()
    {
        if (_subscribedTransit != null) _subscribedTransit.onTraverse.RemoveListener(OnTraverse);
        _subscribedTransit = null;
    }

    private void Update()
    {
        CaptureSafePose();
        RefreshPrompt();
        if (CanControl && IsInRange && (Keyboard.current?.fKey.wasPressedThisFrame ?? false)) TryInteract();
    }

    public bool TryInteract()
    {
        var activation = Activation;
        if (!CanControl || !IsInRange || activation == null || !activation.isActiveAndEnabled
            || activation.State == GateState.Opening || activation.State == GateState.Closing) return false;
        CaptureSafePose();
        bool operated = activation.State == GateState.Closed ? activation.Open() : activation.Close();
        LastError = operated ? null : activation.LastResult;
        RefreshPrompt();
        return operated;
    }

    private void OnTraverse() { if (Transit != null) TryStartRun(Transit.LastTraveller); }

    public bool TryStartRun(Transform traveller)
    {
        if (!CanControl || traveller == null || traveller != player.transform || Activation == null
            || !Activation.isActiveAndEnabled || Activation.State != GateState.Open) return false;
        CaptureSafePose();
        string error = null;
        if (settings == null || !string.Equals(settings.targetScene?.Trim(), GameSceneName, StringComparison.Ordinal)
            || !Application.CanStreamedLevelBeLoaded(GameSceneName))
            error = "탐사 씬을 찾을 수 없습니다. 게이트 목적지와 빌드 씬 설정을 확인해 주세요.";
        else
        {
            var service = PlayerUpgradeService.Instance;
            if (!service.IsReady && !service.TryReload(out error))
                error ??= "강화 데이터를 불러오지 못했습니다. 다시 통과해 주세요.";
        }
        if (error != null) { RestoreHub(error); return false; }
        if (!_hasSafePose) { LastError = "안전한 출발 위치를 확인하지 못했습니다. 거점 쪽에서 다시 접근해 주세요."; RefreshPrompt(); return false; }
        IsLoading = true;
        LastError = null;
        _sceneLoadCommitted = false;
        workshop?.Close();
        player.BeginStageTransition();
        _ownsTransition = true;
        world?.SetModalOpen(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        ShowPrompt(false);
        _loading = StartCoroutine(LoadRun());
        return true;
    }

    private IEnumerator LoadRun()
    {
        if (loadingOverlay != null)
        {
            loadingOverlay.gameObject.SetActive(true);
            loadingOverlay.ShowLoading();
            loadingOverlay.Title.text = "탐사 준비 중";
            loadingOverlay.Details.text = "영구 강화를 유지한 채 구역으로 이동합니다.";
            loadingOverlay.ActionButton.gameObject.SetActive(false);
            loadingOverlay.StageLabel.gameObject.SetActive(false);
            loadingOverlay.ModalRoot.SetActive(true);
        }
        Canvas.ForceUpdateCanvases();
        yield return null;
        AsyncOperation operation = null;
        string error = null;
        try
        {
            CombatHitStopService.SetExternalWorldTimeScale(1f);
            operation = SceneLoaderOverride != null ? SceneLoaderOverride(GameSceneName)
                : SceneManager.LoadSceneAsync(GameSceneName, LoadSceneMode.Single);
        }
        catch (Exception failure) { error = "탐사 씬 이동 실패: " + failure.Message; }
        if (operation == null)
        {
            _loading = null;
            RestoreHub(error ?? "탐사 씬을 불러오지 못했습니다. 다시 통과해 주세요.");
            yield break;
        }
        _sceneLoadCommitted = true;
        while (!operation.isDone) yield return null;
    }

    private void CaptureSafePose()
    {
        if (!CanControl || Transit == null || player.Controller == null || !player.Controller.enabled || !player.IsGround()) return;
        float scale = Mathf.Max(.0001f, Mathf.Abs(Transit.transform.lossyScale.z));
        float radius = player.Controller.radius * Mathf.Max(Mathf.Abs(player.transform.lossyScale.x), Mathf.Abs(player.transform.lossyScale.z));
        float distance = Transit.transform.InverseTransformPoint(player.transform.position).z - Transit.crossingPlaneOffset;
        if (distance < Transit.armDistance + (radius + player.Controller.skinWidth) / scale) return;
        _safePosition = player.transform.position;
        _safeRotation = player.transform.rotation;
        _hasSafePose = true;
    }

    private void RestoreHub(string error)
    {
        LastError = error;
        IsLoading = false;
        _sceneLoadCommitted = false;
        if (loadingOverlay != null) loadingOverlay.Hide();
        if (player != null && _hasSafePose && player.StatManager != null && player.StatManager.CurrentHp > 0f)
        {
            if (!player.IsStageTransitioning) player.BeginStageTransition();
            player.CompleteStageTransition(_safePosition, _safeRotation);
        }
        _ownsTransition = false;
        world?.SetModalOpen(false);
        RefreshPrompt();
    }

    private void RefreshPrompt()
    {
        var activation = Activation;
        bool nearGate = false;
        if (PlayerAvailable && Transit != null)
        {
            Vector3 delta = player.transform.position - Transit.CrossingPlanePosition;
            float radius = Mathf.Max(interactionRadius, Transit.apertureRadius + .75f);
            nearGate = Mathf.Abs(delta.y) <= 2f && new Vector2(delta.x, delta.z).sqrMagnitude <= radius * radius;
        }
        bool visible = CanControl && activation != null && (IsInRange || (nearGate && activation.State != GateState.Closed));
        ShowPrompt(visible);
        if (!visible) return;
        if (_keycap == null && prompt != null)
            _keycap = prompt.transform.Find("Keycap")?.GetComponent<CanvasGroup>();
        if (_keycap != null)
            _keycap.alpha = IsInRange && (activation.State == GateState.Closed || activation.State == GateState.Open) ? 1f : 0f;
        if (actionLabel != null)
            actionLabel.text = activation.State == GateState.Closed ? "게이트 열기"
                : activation.State == GateState.Opening ? "게이트 개방 중…"
                : activation.State == GateState.Closing ? "게이트 폐쇄 중…"
                : IsInRange ? "게이트 닫기" : "게이트 개방 완료";
        if (statusLabel != null)
            statusLabel.text = LastError ?? (activation.State == GateState.Open && activation.CanTraverse
                ? "문 안쪽으로 걸어 들어가면 탐사를 시작합니다." : activation.LastResult);
    }

    private void ShowPrompt(bool visible) { if (prompt != null && prompt.activeSelf != visible) prompt.SetActive(visible); }

    private void OnDisable()
    {
        Unsubscribe();
        if (_loading != null) StopCoroutine(_loading);
        _loading = null;
        // A scene operation cannot be cancelled once accepted. Let that destination own its player.
        if (IsLoading && !_sceneLoadCommitted && _ownsTransition) RestoreHub("탐사 이동이 취소되었습니다.");
        ShowPrompt(false);
    }
}
