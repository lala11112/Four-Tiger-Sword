using UnityEngine;

/// <summary>An explicit exit area, independent of optional exploration room completion.</summary>
[DisallowMultipleComponent]
public sealed class StageExit : MonoBehaviour
{
    public bool IsAvailable { get; private set; }
    private StageManager _stage;
    private PlayerController _player;
    private int _generation;
    private BoxCollider _trigger;
    private Material _material;

    public void Initialize(StageManager stage, PlayerController player, Vector3 size, int generation)
    {
        // A Resources material keeps its shader in player builds; Shader.Find alone does not.
        var markerMaterial = Resources.Load<Material>("StageExitMarker");
        if (markerMaterial == null || markerMaterial.shader == null)
            throw new System.InvalidOperationException("Stage exit marker material is missing or has no shader.");

        _stage = stage; _player = player; _generation = generation;
        _trigger = gameObject.AddComponent<BoxCollider>();
        _trigger.isTrigger = true;
        _trigger.size = size;
        _trigger.center = Vector3.up * size.y * 0.5f;
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "ExitMarker";
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = Vector3.up * 0.04f;
        visual.transform.localScale = new Vector3(size.x, 0.025f, size.z);
        var collider = visual.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        var renderer = visual.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _material = new Material(markerMaterial);
        renderer.sharedMaterial = _material;
        SetAvailable(false);
    }

    public void SetAvailable(bool available)
    {
        IsAvailable = available;
        if (_material != null)
        {
            var color = available ? new Color(0.1f, 0.9f, 0.8f) : new Color(0.3f, 0.32f, 0.35f);
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", color);
            else _material.color = color;
        }
    }

    private void Update()
    {
        // Explicit feet overlap works with CharacterController without an extra Rigidbody.
        if (!IsAvailable || _stage == null || _player == null || !_player.Controller.enabled || _player.IsStageTransitioning) return;
        if (ContainsPlayer(_player)) _stage.TryUseExit(_player, _generation);
    }

    public bool ContainsPlayer(PlayerController player)
    {
        if (player == null || player != _player || player.Controller == null || _trigger == null) return false;
        var controller = player.Controller;
        Vector3 feetWorld = player.transform.TransformPoint(controller.center)
            - Vector3.up * (controller.height * Mathf.Abs(player.transform.lossyScale.y) * 0.5f);
        Vector3 feet = transform.InverseTransformPoint(feetWorld);
        Vector3 half = _trigger.size * 0.5f;
        return Mathf.Abs(feet.x) <= half.x && Mathf.Abs(feet.z) <= half.z && feet.y >= -0.3f && feet.y <= _trigger.size.y;
    }

    private void OnDestroy() { if (_material != null) Destroy(_material); }
}
