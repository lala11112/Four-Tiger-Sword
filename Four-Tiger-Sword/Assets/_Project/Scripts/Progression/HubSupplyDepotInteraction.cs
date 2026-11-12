using System.Collections.Generic;
using BoundaryZero.SupplyDepot;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Opens the depot from its Hub station and owns the modal while it is open.</summary>
[DisallowMultipleComponent]
public sealed class HubSupplyDepotInteraction : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private Transform station;
    [SerializeField] private HubWorldController world;
    [SerializeField] private SupplyDepotController depot;
    [SerializeField] private HubSupplyDepotAccount account;
    [SerializeField] private GameObject prompt;
    [SerializeField] private GameObject dimmer;
    [SerializeField] private TMP_Text status;
    [SerializeField] private Vector3 localAnchor;
    [SerializeField, Min(.1f)] private float interactionRadius = 1.4f;

    private readonly List<CinemachineInputAxisController> _cameraInputs = new();
    private CombatHitStopService.WorldScaleLease _pause;

    public bool IsOpen => _pause != null && depot != null && depot.IsOpen;
    public Vector3 InteractionPosition => station != null ? station.TransformPoint(localAnchor) : transform.position;
    public bool IsInRange
    {
        get
        {
            if (!PlayerAvailable || station == null || !station.gameObject.activeInHierarchy) return false;
            Vector3 delta = player.transform.position - InteractionPosition;
            return Mathf.Abs(delta.y) <= 2f && new Vector2(delta.x, delta.z).sqrMagnitude <= interactionRadius * interactionRadius;
        }
    }

    private bool PlayerAvailable => player != null && player.isActiveAndEnabled && !player.IsStageTransitioning
        && player.StatManager != null && player.StatManager.CurrentHp > 0f;

    private bool HasActiveHost => world != null && world.isActiveAndEnabled && world.PauseMenuPlayer == player
        && depot != null && depot.isActiveAndEnabled && account != null && account.isActiveAndEnabled;

    private bool CanOpen => isActiveAndEnabled && _pause == null && HasActiveHost && IsInRange
        && !HubReturnTransition.IsActive && !world.IsModalOpen && !player.IsGameplayModalPaused && !player.IsFinisherSelecting;

    public void Configure(PlayerController targetPlayer, Transform targetStation, HubWorldController worldController,
        SupplyDepotController controller, HubSupplyDepotAccount provider, GameObject interactionPrompt,
        GameObject modalDimmer, TMP_Text feedback, Vector3 stationLocalAnchor, float radius)
    {
        player = targetPlayer;
        station = targetStation;
        world = worldController;
        depot = controller;
        account = provider;
        prompt = interactionPrompt;
        dimmer = modalDimmer;
        status = feedback;
        localAnchor = stationLocalAnchor;
        interactionRadius = Mathf.Max(.1f, radius);
    }

    private void OnEnable()
    {
        SetVisible(prompt, false);
        SetVisible(dimmer, false);
        if (depot != null)
        {
            depot.OpenChanged += OnOpenChanged;
            depot.BindAccount(account);
        }
        if (account != null) account.StatusChanged += RefreshStatus;
        RefreshStatus();
    }

    private void Update()
    {
        if (_pause != null)
        {
            if (!IsOpen || !HasActiveHost || !IsInRange || !_pause.IsValid) Close();
            return;
        }

        bool available = CanOpen;
        SetVisible(prompt, available);
        if (available && Keyboard.current?.fKey.wasPressedThisFrame == true) TryOpen();
    }

    public bool TryOpen()
    {
        if (!CanOpen) return false;
        account.RefreshAccount();
        if (!CanOpen) return false;

        var pause = CombatHitStopService.AcquireWorldScale(this, 0f);
        if (!pause.IsValid)
        {
            pause.Dispose();
            return false;
        }

        _pause = pause;
        try
        {
            player.SetShopPaused(true);
            foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!TracksPlayer(camera.Follow) && !TracksPlayer(camera.LookAt)) continue;
                foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
                {
                    if (!input.enabled) continue;
                    _cameraInputs.Add(input);
                    input.enabled = false;
                }
            }
            world.SetModalOpen(true);
            return depot.Open() && IsOpen;
        }
        finally
        {
            if (!IsOpen) ReleaseModal();
        }
    }

    public void Close()
    {
        try
        {
            if (depot != null && depot.IsOpen)
            {
                depot.SkipAnimation();
                depot.Close();
            }
        }
        finally { ReleaseModal(); }
    }

    private void OnOpenChanged(bool open)
    {
        if (!open)
        {
            ReleaseModal();
            return;
        }
        SetVisible(prompt, false);
        SetVisible(dimmer, true);
        RefreshStatus();
    }

    private void ReleaseModal()
    {
        SetVisible(dimmer, false);
        if (status != null) status.text = string.Empty;
        var pause = _pause;
        if (pause == null) return;
        _pause = null;

        var eventSystem = EventSystem.current;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && depot != null && selected.transform.IsChildOf(depot.transform))
            eventSystem.SetSelectedGameObject(null);
        if (player != null) player.SetShopPaused(false);
        if (PlayerAvailable)
            foreach (var input in _cameraInputs)
                if (input != null) input.enabled = true;
        _cameraInputs.Clear();
        pause.Dispose();
        if (world != null) world.SetModalOpen(false);
    }

    private bool TracksPlayer(Transform target) => target != null
        && (target == player.transform || target.IsChildOf(player.transform));

    private void RefreshStatus()
    {
        if (status == null) return;
        status.text = IsOpen && account != null ? account.LastError ?? string.Empty : string.Empty;
    }

    private void OnDisable()
    {
        if (depot != null) depot.OpenChanged -= OnOpenChanged;
        if (account != null) account.StatusChanged -= RefreshStatus;
        Close();
        SetVisible(prompt, false);
    }

    private static void SetVisible(GameObject target, bool visible)
    {
        if (target != null && target.activeSelf != visible) target.SetActive(visible);
    }
}
