using System;
using LabDoor;
using StudioBaekho;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Owns one room entrance, its physical lock, and a safe inward entry plane.</summary>
[DisallowMultipleComponent]
public sealed class DungeonEncounterGate : MonoBehaviour
{
    private const float EntryMargin = 0.05f;
    private const float InteriorDepth = 2f;

    private SlidingDoor _door;
    private GameObject _doorRoot;
    private BoxCollider _blocker;
    private NavMeshObstacle _obstacle;
    private float _halfWidth;
    private float _height;
    private float _closedInnerDepth;
    private bool _initialized;

    public bool IsLocked { get; private set; }
    public GameObject DoorRoot => _doorRoot;

    public void Initialize(BaekhoMapConnection socket, float clearWidth, GameObject doorPrefab)
    {
        if (_initialized) throw new InvalidOperationException("This encounter gate is already initialized.");
        if (socket == null) throw new ArgumentNullException(nameof(socket));
        if (!IsPositive(clearWidth) || !IsPositive(socket.width) || !IsPositive(socket.height))
            throw new ArgumentException("Encounter gate openings must have finite, positive dimensions.");

        GameObject source = socket.runtimeDoor != null ? socket.runtimeDoor : doorPrefab;
        if (source == null) throw new ArgumentException("Assign a runtime door or an encounter door prefab.");
        var sourceDoor = source.GetComponentInChildren<SlidingDoor>(true);
        if (sourceDoor == null || sourceDoor.LeftLeaf == null || sourceDoor.RightLeaf == null)
            throw new ArgumentException("An encounter door requires a SlidingDoor with both leaves assigned.");

        transform.SetPositionAndRotation(socket.transform.position, socket.transform.rotation);
        transform.localScale = Vector3.one;
        float openingWidth = Mathf.Min(clearWidth, socket.width);
        _halfWidth = openingWidth * 0.5f;
        _height = socket.height;
        if (socket.runtimeDoor != null)
        {
            // Explicitly authored doors may be recessed into a thick room wall.
            _doorRoot = socket.runtimeDoor;
        }
        else
        {
            _doorRoot = Instantiate(doorPrefab, transform, false);
            _doorRoot.name = "EncounterDoor_" + socket.id;
            _doorRoot.transform.localPosition = GetWallAlignedDoorPosition(socket);
            _doorRoot.transform.localRotation = Quaternion.identity;
            _doorRoot.transform.localScale = new Vector3(openingWidth / 4f, _height / 3f,
                doorPrefab.transform.localScale.z);
        }
        _door = _doorRoot.GetComponentInChildren<SlidingDoor>(true);
        _door.SetOpenImmediate(false);
        Bounds closedBounds = MeasureClosedLeaves();
        _closedInnerDepth = -closedBounds.min.z;

        var barrier = new GameObject("EncounterLock_" + socket.id);
        barrier.transform.SetParent(transform, false);
        _blocker = barrier.AddComponent<BoxCollider>();
        _blocker.center = new Vector3(0f, _height * 0.5f, closedBounds.center.z);
        _blocker.size = new Vector3(openingWidth + 0.02f, _height, Mathf.Max(0.1f, closedBounds.size.z));
        _blocker.enabled = false;
        _obstacle = barrier.AddComponent<NavMeshObstacle>();
        _obstacle.enabled = false;
        _obstacle.shape = NavMeshObstacleShape.Box;
        _obstacle.center = _blocker.center;
        _obstacle.size = _blocker.size;
        _obstacle.carving = true;
        _obstacle.carveOnlyStationary = false;

        _initialized = true;
        Open(true);
        _doorRoot.SetActive(true);
    }

    public void Lock()
    {
        if (!_initialized) return;
        IsLocked = true;
        _blocker.enabled = true;
        _obstacle.enabled = true;
        _door.Close();
    }

    public void Open(bool immediate = false)
    {
        IsLocked = false;
        if (_blocker != null) _blocker.enabled = false;
        if (_obstacle != null) _obstacle.enabled = false;
        if (_door == null) return;
        if (immediate) _door.SetOpenImmediate(true);
        else _door.Open();
    }

    /// <summary>The supplied positions are the CharacterController capsule center in world space.</summary>
    public bool HasEntered(CharacterController controller, Vector3 previousCenter, Vector3 currentCenter)
    {
        if (!_initialized || controller == null || !controller.enabled) return false;
        Vector3 previous = transform.InverseTransformPoint(previousCenter);
        Vector3 current = transform.InverseTransformPoint(currentCenter);
        GetCapsuleSupport(controller, out Vector3 support, out float skin);
        float plane = _closedInnerDepth + support.z + skin + EntryMargin;
        float previousDepth = -previous.z;
        float currentDepth = -current.z;
        if (previousDepth > plane || currentDepth < plane || currentDepth <= previousDepth) return false;
        float fraction = (plane - previousDepth) / (currentDepth - previousDepth);
        return WithinOpening(Vector3.LerpUnclamped(previous, current, fraction), support);
    }

    /// <summary>A bounded inner doorway area, for a player first observed just past the entry plane.</summary>
    public bool ContainsEntryInterior(CharacterController controller, Vector3 center)
    {
        if (!_initialized || controller == null || !controller.enabled) return false;
        Vector3 local = transform.InverseTransformPoint(center);
        GetCapsuleSupport(controller, out Vector3 support, out float skin);
        float plane = _closedInnerDepth + support.z + skin + EntryMargin;
        float depth = -local.z;
        return depth >= plane && depth <= plane + InteriorDepth && WithinOpening(local, support);
    }

    public void Dispose()
    {
        Open(true);
        _initialized = false;
    }

    private void OnDisable() => Open(true);
    private void OnDestroy() => Open(true);

    private Vector3 GetWallAlignedDoorPosition(BaekhoMapConnection socket)
    {
        var module = socket.GetComponentInParent<BaekhoMapModule>(true);
        var wall = socket.closedWall;
        if (module == null || wall == null
            || !IsPositive(wall.size.x) || !IsPositive(wall.size.y) || !IsPositive(wall.size.z))
            return Vector3.zero;

        // Sockets mark the corridor attachment at the outside of the wall. Recess the
        // door to the prepared wall's center plane, keeping the aperture's floor and width center.
        Vector3 wallCenter = transform.InverseTransformPoint(module.transform.TransformPoint(wall.localCenter));
        return new Vector3(0f, 0f, wallCenter.z);
    }

    private bool WithinOpening(Vector3 point, Vector3 support)
    {
        // The whole capsule must clear the closing plane, while its silhouette only needs
        // to overlap the aperture laterally: a controller may legally hug the jamb within its skin.
        return Mathf.Abs(point.x) - support.x < _halfWidth
            && point.y + support.y > EntryMargin
            && point.y - support.y < _height - EntryMargin;
    }

    private void GetCapsuleSupport(CharacterController controller, out Vector3 support, out float skin)
    {
        Vector3 scale = controller.transform.lossyScale;
        float radialScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float radius = controller.radius * radialScale;
        float halfSegment = Mathf.Max(0f, controller.height * Mathf.Abs(scale.y) * 0.5f - radius);
        Vector3 axis = transform.InverseTransformDirection(controller.transform.up).normalized;
        support = new Vector3(radius + halfSegment * Mathf.Abs(axis.x),
            radius + halfSegment * Mathf.Abs(axis.y), radius + halfSegment * Mathf.Abs(axis.z));
        skin = controller.skinWidth * Mathf.Max(radialScale, Mathf.Abs(scale.y));
    }

    private Bounds MeasureClosedLeaves()
    {
        Bounds result = default;
        bool found = false;
        foreach (Transform leaf in new[] { _door.LeftLeaf, _door.RightLeaf })
        {
            bool leafFound = false;
            foreach (Collider collider in leaf.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || collider.isTrigger) continue;
                Bounds local;
                if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
                else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
                else continue;
                AddBounds(collider.transform, local, ref result, ref found);
                leafFound = true;
            }
            if (leafFound) continue;
            foreach (MeshFilter mesh in leaf.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.sharedMesh == null) continue;
                AddBounds(mesh.transform, mesh.sharedMesh.bounds, ref result, ref found);
                leafFound = true;
            }
            if (!leafFound)
                throw new ArgumentException("Each encounter door leaf must contain a box collider or mesh.");
        }
        return result;
    }

    private void AddBounds(Transform source, Bounds local, ref Bounds result, ref bool found)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            Vector3 point = transform.InverseTransformPoint(source.TransformPoint(corner));
            if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
            else result.Encapsulate(point);
        }
    }

    private static bool IsPositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}
