using System;
using EasternLab.GateAddon;
using UnityEngine;

/// <summary>Opens on end-room arrival and delegates a verified walking traversal to the current stage.</summary>
[DisallowMultipleComponent]
public sealed class StageExit : MonoBehaviour
{
    [SerializeField] private GateActivation activation;
    [SerializeField] private GateTransit transit;
    [SerializeField] private SpatialPortalView portalView;
    public GateActivation Activation => activation;
    public GateTransit Transit => transit;
    public SpatialPortalView PortalView => portalView;
    public bool IsAvailable => isActiveAndEnabled && _requestedAvailable && activation != null
        && activation.isActiveAndEnabled && activation.State == GateState.Open;
    private StageManager _stage;
    private PlayerController _player;
    private int _generation;
    private bool _requestedAvailable, _dispatchingTraversal;

    public void ConfigurePortal(GateActivation gate, GateTransit passage, SpatialPortalView view)
    { activation = gate; transit = passage; portalView = view; }

    public void Initialize(StageManager stage, PlayerController player, int generation)
    {
        if (activation == null || transit == null || portalView == null || transit.activation != activation
            || activation.transit != transit || activation.apertureBlocker == null
            || portalView.exitFrame == null || portalView.windowRenderer == null
            || transit.GetComponent<BoxCollider>() == null)
            throw new InvalidOperationException("Stage exit portal is missing its gate, passage or spatial view references.");
        if (stage == null || player == null || player.Controller == null)
            throw new InvalidOperationException("Stage exit portal requires its stage and player.");
        _stage = stage;
        _player = player;
        _generation = generation;
        transit.eventOnlyTraversal = true;
        transit.playConversionEffect = false;
        transit.playerRoot = player.transform;
        transit.playerCamera = Camera.main;
        portalView.playerCamera = transit.playerCamera;
        transit.TraversalFilter = CanTraverse;
        transit.onTraverse.RemoveListener(OnTraverse);
        transit.onTraverse.AddListener(OnTraverse);
        SetAvailable(false);
    }

    public void SetAvailable(bool available)
    {
        available &= isActiveAndEnabled && _stage != null;
        if (!available)
        {
            _requestedAvailable = false;
            if (transit != null) transit.enabled = false;
            if (activation != null)
            {
                activation.enabled = false;
                if (activation.apertureBlocker != null) activation.apertureBlocker.enabled = true;
            }
            return;
        }
        if (_requestedAvailable) return;
        _requestedAvailable = true;
        activation.enabled = true;
        transit.enabled = true;
        activation.Open();
    }

    private bool CanTraverse(Transform traveller)
        => IsAvailable && _player != null && traveller == _player.transform
            && _stage != null && _stage.CanUseExit(_player, _generation);

    private void OnTraverse()
    {
        if (!CanTraverse(transit.LastTraveller) || !ContainsPlayer(_player)) return;
        _dispatchingTraversal = true;
        try { _stage.TryUseExit(_player, _generation); }
        finally { _dispatchingTraversal = false; }
    }

    internal bool HasVerifiedTraversal(PlayerController player)
        => _dispatchingTraversal && player == _player && transit != null && transit.LastTraveller == player.transform;

    /// <summary>Checks the aperture and crossing side; GateTransit supplies proof of the walking traversal.</summary>
    public bool ContainsPlayer(PlayerController player)
    {
        if (player == null || player != _player || player.Controller == null || transit == null) return false;
        if (!transit.FitsAperture(player.Controller)) return false;
        float localZ = transit.transform.InverseTransformPoint(player.transform.position).z;
        // A fast CharacterController move may already be beyond the trigger when OnTriggerExit dispatches.
        return localZ <= transit.crossingPlaneOffset - transit.crossingThreshold + .01f;
    }

    private void OnDisable() => SetAvailable(false);

    private void OnDestroy()
    {
        if (transit == null) return;
        transit.onTraverse.RemoveListener(OnTraverse);
        transit.TraversalFilter = null;
    }
}
