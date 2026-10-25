using UnityEngine;

/// <summary>Authored locations used by the reused game scene's stage flow.</summary>
[DisallowMultipleComponent]
public sealed class StageMapAnchors : MonoBehaviour
{
    [Tooltip("Exact player root position after generation. Place the full CharacterController above the floor.")]
    public Transform playerSpawn;
    [Tooltip("Floor position at the center of the exit area.")]
    public Transform exitPoint;
    [Tooltip("Floor position. The boss agent base offset is applied automatically.")]
    public Transform bossSpawn;
    [Tooltip("Floor position for the shop counter inside a dedicated shop room.")]
    public Transform shopPoint;
    public Vector3 exitSize = new(2f, 3f, 2f);
}
