using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Opens the item codex from its world-space terminal.</summary>
[DisallowMultipleComponent]
public sealed class HubCodexInteraction : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private Transform station;
    [SerializeField] private GameObject prompt;
    [SerializeField] private HubWorldController world;
    [SerializeField] private Vector3 localAnchor;
    [SerializeField, Min(.1f)] private float interactionRadius = 2f;

    public Vector3 InteractionPosition => station != null ? station.TransformPoint(localAnchor) : transform.position;

    public bool IsInRange
    {
        get
        {
            if (player == null || !player.isActiveAndEnabled || player.IsStageTransitioning
                || player.StatManager == null || player.StatManager.CurrentHp <= 0f
                || station == null || !station.gameObject.activeInHierarchy) return false;
            Vector3 delta = player.transform.position - InteractionPosition;
            return Mathf.Abs(delta.y) <= 2f && new Vector2(delta.x, delta.z).sqrMagnitude <= interactionRadius * interactionRadius;
        }
    }

    private bool CanInteract => isActiveAndEnabled && world != null && IsInRange && world.CanOpenCodex(player);

    public void Configure(PlayerController targetPlayer, Transform targetStation, GameObject interactionPrompt,
        HubWorldController worldController, Vector3 stationLocalAnchor, float radius = 2f)
    {
        if (Application.isPlaying) Release();
        player = targetPlayer;
        station = targetStation;
        prompt = interactionPrompt;
        world = worldController;
        localAnchor = stationLocalAnchor;
        interactionRadius = Mathf.Max(.1f, radius);
    }

    private void OnEnable() => ShowPrompt(false);

    private void Update()
    {
        if (world != null && world.OwnsCodex(this) && !IsInRange)
            world.CloseCodex(this);
        bool available = CanInteract;
        ShowPrompt(available);
        if (available && Keyboard.current?.fKey.wasPressedThisFrame == true) TryOpen();
    }

    public bool TryOpen()
    {
        if (!CanInteract || !world.TryOpenCodex(this, player)) return false;
        ShowPrompt(false);
        return true;
    }

    private void OnDisable() => Release();

    private void Release()
    {
        if (world != null) world.CloseCodex(this);
        ShowPrompt(false);
    }

    private void ShowPrompt(bool visible)
    {
        if (prompt != null && prompt.activeSelf != visible) prompt.SetActive(visible);
    }
}
