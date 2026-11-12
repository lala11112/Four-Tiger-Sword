using System.Collections.Generic;
using StudioBaekho;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>A room-owned, single-use interaction for a committed combat reward receipt.</summary>
[DefaultExecutionOrder(-45)] // After input and shop interaction, before the player consumes the shared F key.
public sealed class DungeonRewardChest : MonoBehaviour
{
    internal const float UseDuration = .2f;
    private const float SpawnDuration = .25f;
    private const float InteractionRadius = 2.5f;
    private const float PromptGlyphHeight = .28f;
    private const float MaxPromptViewportHeight = .025f;
    internal const float ModelScale = .8f;
    private RunManager _run;
    private GameObject _prefab;
    private Transform _model;
    private GameObject[] _outlines;
    private readonly List<Collider> _solidColliders = new();
    private TMP_Text _prompt;
    private float _promptBaseScale;
    private Light _useLight;
    private float _nextPlacement, _spawnAge, _useAge;
    private bool _used, _reportedPlacementFailure;

    internal DungeonRoomEncounter Room { get; private set; }
    internal string Receipt { get; private set; }
    internal bool IsAvailable => isActiveAndEnabled && _model != null && !_used && _spawnAge >= SpawnDuration;

    internal static void Spawn(RunManager run, DungeonRoomEncounter room, string receipt, GameObject prefab)
    {
        var root = new GameObject("CombatRewardChest");
        root.transform.SetParent(room.transform, false);
        var chest = root.AddComponent<DungeonRewardChest>();
        chest._run = run;
        chest.Room = room;
        chest.Receipt = receipt;
        chest._prefab = prefab;
        chest.TryPlace();
    }

    private void TryPlace()
    {
        _nextPlacement = Time.unscaledTime + .5f;
        var module = Room.GetComponent<BaekhoMapModule>();
        if (_prefab == null || !DungeonRewardChestPlacement.TryFind(module, out var position, out var rotation))
        {
            if (!_reportedPlacementFailure)
            {
                _reportedPlacementFailure = true;
                Debug.LogWarning($"[Dungeon] Waiting to place combat reward chest in {Room.name}.", Room);
            }
            return;
        }
        transform.SetPositionAndRotation(position, rotation);
        _model = Instantiate(_prefab, transform, false).transform;
        _solidColliders.Clear();
        foreach (var collider in _model.GetComponentsInChildren<Collider>(true))
        {
            if (collider.enabled && !collider.isTrigger) _solidColliders.Add(collider);
            collider.enabled = false;
        }
        _spawnAge = 0f;
        _model.localPosition = Vector3.up * .3f;
        _model.localScale = Vector3.zero;
        _outlines = DungeonRewardChestOutline.Create(_model);
        CreatePrompt();
    }

    private void Update()
    {
        if (_run == null || Room == null) return;
        if (_model == null)
        {
            if (Time.unscaledTime >= _nextPlacement && _run.CanPlaceRewardChest(Room)) TryPlace();
            return;
        }
        AdvancePresentation(Time.unscaledDeltaTime);
        if (_used) return;

        bool near = _run.CanUseRewardChest(this);
        _prompt.gameObject.SetActive(near);
        if (!near) return;
        if (Camera.main is { } camera) FacePrompt(camera);
        if (Keyboard.current?.fKey.wasPressedThisFrame == true) _run.TryOpenRewardChest(this);
    }

    private void FacePrompt(Camera camera)
    {
        var prompt = _prompt.transform;
        prompt.rotation = camera.transform.rotation;
        Vector3 center = camera.WorldToViewportPoint(prompt.position);
        Vector3 top = camera.WorldToViewportPoint(prompt.position + camera.transform.up * PromptGlyphHeight);
        float viewportHeight = Mathf.Abs(top.y - center.y);
        float size = center.z > 0f && viewportHeight > MaxPromptViewportHeight
            ? MaxPromptViewportHeight / viewportHeight : 1f;
        prompt.localScale = Vector3.one * (_promptBaseScale * size);
    }

    private void AdvancePresentation(float delta)
    {
        if (_spawnAge < SpawnDuration)
        {
            _spawnAge += delta;
            float t = Mathf.Clamp01(_spawnAge / SpawnDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            _model.localScale = Vector3.one * (ModelScale * eased);
            _model.localPosition = Vector3.up * (.3f * (1f - eased));
            if (t >= 1f) SetSolidColliders(true);
        }
        if (!_used) return;

        _useAge += delta;
        float use = Mathf.Clamp01(_useAge / UseDuration);
        float pulse = Mathf.Sin(Mathf.PI * use);
        _model.localScale = Vector3.one * (ModelScale * (1f + .08f * pulse));
        _useLight.intensity = 3f * pulse;
        if (use >= 1f) Destroy(gameObject);
    }

    internal bool CanReach(PlayerController player)
    {
        if (player.Controller == null) return false;
        var capsule = player.Controller.bounds;
        Vector3 feet = new(capsule.center.x, capsule.min.y, capsule.center.z);
        Vector3 offset = feet - transform.position;
        if (Mathf.Abs(offset.y) > 1.25f || new Vector2(offset.x, offset.z).sqrMagnitude > InteractionRadius * InteractionRadius)
            return false;

        Vector3 origin = feet + Vector3.up * .8f;
        Vector3 ray = transform.position + Vector3.up * .55f - origin;
        int solidMask = ~LayerMask.GetMask("Player", "Enemy", "PlayerSkill", "EnemySkill");
        return !gameObject.scene.GetPhysicsScene().Raycast(origin, ray.normalized, out var hit, ray.magnitude,
            solidMask, QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(transform);
    }

    internal void BeginUse()
    {
        _used = true;
        SetSolidColliders(false);
        foreach (var outline in _outlines) outline.SetActive(false);
        _prompt.gameObject.SetActive(false);
        var lightRoot = new GameObject("RewardGlow", typeof(Light));
        lightRoot.transform.SetParent(transform, false);
        lightRoot.transform.localPosition = Vector3.up * .72f;
        _useLight = lightRoot.GetComponent<Light>();
        _useLight.type = LightType.Point;
        _useLight.color = new Color(1f, .67f, .24f);
        _useLight.range = 2f;
        _useLight.intensity = 0f;
    }

    private void SetSolidColliders(bool enabled)
    {
        foreach (var collider in _solidColliders)
            if (collider != null) collider.enabled = enabled;
    }

    private void CreatePrompt()
    {
        var promptRoot = new GameObject("InteractionPrompt", typeof(TextMeshPro));
        promptRoot.transform.SetParent(transform, false);
        promptRoot.transform.localPosition = Vector3.up * 1.05f;
        _prompt = promptRoot.GetComponent<TextMeshPro>();
        _prompt.font = Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/BoundaryFont") ?? TMP_Settings.defaultFontAsset;
        _prompt.fontSize = 24f;
        _prompt.alignment = TextAlignmentOptions.Center;
        _prompt.textWrappingMode = TextWrappingModes.NoWrap;
        _prompt.color = new Color32(255, 230, 180, 255);
        _prompt.text = "[F] 보상 상자 열기";
        _prompt.ForceMeshUpdate();
        float glyphHeight = _prompt.textBounds.size.y;
        _promptBaseScale = glyphHeight > .01f ? PromptGlyphHeight / glyphHeight : .12f;
        promptRoot.transform.localScale = Vector3.one * _promptBaseScale;
        promptRoot.SetActive(false);
    }
}
