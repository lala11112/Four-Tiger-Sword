using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Opens the permanent upgrade workshop from its world-space interaction point.</summary>
[DisallowMultipleComponent]
public sealed class HubWorkshopInteraction : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private Transform station;
    [SerializeField] private HubWorkshopView view;
    [SerializeField] private GameObject prompt;
    [SerializeField] private HubWorldController world;
    [SerializeField] private Vector3 localAnchor;
    [SerializeField, Min(.1f)] private float interactionRadius = 2f;

    private readonly List<CinemachineInputAxisController> _cameraInputs = new();
    private CombatHitStopService.WorldScaleLease _pause;
    private bool _ownsModal;
    private bool _closing;
    private int _closeVersion;

    public bool IsOpen => view != null && view.IsOpen;
    public Vector3 InteractionPosition => station != null ? station.TransformPoint(localAnchor) : transform.position;
    public bool IsInRange
    {
        get
        {
            if (player == null || station == null || !station.gameObject.activeInHierarchy
                || !player.isActiveAndEnabled || player.IsStageTransitioning
                || player.StatManager == null || player.StatManager.CurrentHp <= 0f) return false;
            Vector3 delta = player.transform.position - InteractionPosition;
            return Mathf.Abs(delta.y) <= 2f && new Vector2(delta.x, delta.z).sqrMagnitude <= interactionRadius * interactionRadius;
        }
    }

    public void Configure(PlayerController targetPlayer, Transform targetStation, HubWorkshopView targetView,
        GameObject interactionPrompt, HubWorldController worldController, Vector3 stationLocalAnchor, float radius = 2f)
    {
        player = targetPlayer;
        station = targetStation;
        view = targetView;
        prompt = interactionPrompt;
        world = worldController;
        localAnchor = stationLocalAnchor;
        interactionRadius = Mathf.Max(.1f, radius);
    }

    private void OnEnable()
    {
        if (view != null) view.CloseRequested += RequestClose;
    }

    private void Start()
    {
        if (view != null)
        {
            view.Bind(PlayerUpgradeService.Instance);
            view.Close();
        }
        ShowPrompt(false);
    }

    private void Update()
    {
        if (_ownsModal)
        {
            if (!IsOpen || !IsInRange || _pause == null || !_pause.IsValid
                || world == null || !world.isActiveAndEnabled) Close();
            else if (!_closing && (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)) RequestClose();
            return;
        }
        bool available = IsInRange && !player.IsGameplayModalPaused && view != null;
        ShowPrompt(available);
        if (available && (Keyboard.current?.fKey.wasPressedThisFrame ?? false)) TryOpen();
    }

    public bool TryOpen()
    {
        if (_ownsModal || !isActiveAndEnabled || !IsInRange || view == null || world == null
            || player.IsGameplayModalPaused) return false;
        player.CancelFinisherSelection();
        _closeVersion++;
        _closing = false;
        _ownsModal = true;
        try
        {
            player.SetShopPaused(true);
            _pause = CombatHitStopService.AcquireWorldScale(this, 0f);
            if (!_pause.IsValid) { Close(); return false; }
            foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            {
                if (!TracksPlayer(camera.Follow) && !TracksPlayer(camera.LookAt)) continue;
                foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
                    if (input.enabled) { _cameraInputs.Add(input); input.enabled = false; }
            }
            ShowPrompt(false);
            world.SetModalOpen(true);
            view.Open();
            return true;
        }
        catch
        {
            Close();
            throw;
        }
    }

    private bool TracksPlayer(Transform target)
        => target != null && (target == player.transform || target.IsChildOf(player.transform));

    public void Close()
    {
        _closeVersion++;
        _closing = false;
        if (view != null) view.Close();
        if (!_ownsModal) return;
        _ownsModal = false;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (player != null) player.SetShopPaused(false);
        if (player != null && player.isActiveAndEnabled && !player.IsStageTransitioning
            && player.StatManager != null && player.StatManager.CurrentHp > 0f)
            foreach (var input in _cameraInputs)
                if (input != null) input.enabled = true;
        _cameraInputs.Clear();
        _pause?.Dispose();
        _pause = null;
        if (world != null) world.SetModalOpen(false);
        ShowPrompt(isActiveAndEnabled && IsInRange);
    }

    public void RequestClose()
    {
        if (!_ownsModal || _closing || view == null) return;
        _closing = true;
        int version = ++_closeVersion;
        view.PlayClose(() =>
        {
            if (!_ownsModal || !_closing || version != _closeVersion) return;
            Close();
        });
    }

    private void ShowPrompt(bool visible)
    {
        if (prompt != null && prompt.activeSelf != visible) prompt.SetActive(visible);
    }

    private void OnDisable()
    {
        Close();
        ShowPrompt(false);
        if (view != null) view.CloseRequested -= RequestClose;
    }
}
