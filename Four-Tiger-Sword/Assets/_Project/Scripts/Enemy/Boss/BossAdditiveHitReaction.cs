using UnityEngine;

/// <summary>Adds a short torso recoil without changing the boss's action or base animation state.</summary>
[DisallowMultipleComponent]
public sealed class BossAdditiveHitReaction : MonoBehaviour
{
    public Enemy owner;
    public Animator animator;
    public string layerName = "HitReaction";
    public string stateName = "Hit";
    public string timeParameterName = "HitReactionTime";
    [Range(0f, 1f)] public float maximumWeight = .8f;
    [Min(.05f)] public float duration = .24f;
    [Min(0f)] public float blendIn = .018f;
    [Min(0f)] public float blendOut = .045f;
    [Min(0f)] public float minimumRetriggerInterval = .045f;
    [Min(1f)] public float criticalMultiplier = 1.2f;

    public bool IsReacting => _reacting;
    public int LayerIndex => _layerIndex;
    public float MotionTime => _sampleTime;
    public float CurrentWeight => animator != null && _layerIndex >= 0
        ? animator.GetLayerWeight(_layerIndex) : 0f;

    private Enemy _subscribedOwner;
    private RuntimeAnimatorController _controller;
    private int _layerIndex = -1;
    private int _stateHash, _timeParameterHash;
    private float _elapsed, _strength, _startWeight, _sampleTime, _sampleStart;
    private bool _reacting, _hasTimeParameter;

    private void Awake() => FindReferences();

    private void OnEnable()
    {
        FindReferences();
        CacheLayer();
        ClearReaction();
        _subscribedOwner = owner;
        if (_subscribedOwner != null)
        {
            _subscribedOwner.OnDamaged += OnDamaged;
            _subscribedOwner.OnDied += ClearReaction;
        }
    }

    private void OnDisable()
    {
        if (_subscribedOwner != null)
        {
            _subscribedOwner.OnDamaged -= OnDamaged;
            _subscribedOwner.OnDied -= ClearReaction;
        }
        _subscribedOwner = null;
        ClearReaction();
    }

    private void FindReferences()
    {
        if (owner == null) owner = GetComponentInParent<Enemy>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator == null && owner != null) animator = owner.GetComponentInChildren<Animator>(true);
    }

    /// <summary>Moves the overlay to the active phase's rig without leaving weight on the old one.</summary>
    public void BindAnimator(Animator target)
    {
        ClearReaction();
        animator = target;
        CacheLayer();
        ClearReaction();
    }

    private void CacheLayer()
    {
        _controller = animator != null ? animator.runtimeAnimatorController : null;
        _layerIndex = animator != null && _controller != null ? animator.GetLayerIndex(layerName) : -1;
        _stateHash = Animator.StringToHash(layerName + "." + stateName);
        _timeParameterHash = Animator.StringToHash(timeParameterName);
        _hasTimeParameter = false;
        if (animator == null || _controller == null) return;
        foreach (var parameter in animator.parameters)
            if (parameter.nameHash == _timeParameterHash && parameter.type == AnimatorControllerParameterType.Float)
                _hasTimeParameter = true;
    }

    private bool CanReact => isActiveAndEnabled && owner != null && owner.isActiveAndEnabled
        && !owner.IsDead && owner.EnemyStat != null && owner.EnemyStat.CurrentHp > 0f
        && !owner.IsGroggy && !owner.PendingGroggy
        && (!(owner is Boss boss) || !boss.IsTransitioning)
        && animator != null && animator.isActiveAndEnabled && _layerIndex >= 0 && _hasTimeParameter;

    private void OnDamaged(float damage, ElementType element, bool critical)
    {
        if (damage > 0f) TriggerHit(critical ? criticalMultiplier : 1f);
    }

    /// <summary>Also exposed for a preview rig; strength changes presentation only.</summary>
    public bool TriggerHit(float strength = 1f)
    {
        FindReferences();
        if (animator != null && (animator.runtimeAnimatorController != _controller || _layerIndex < 0))
            CacheLayer();
        if (!CanReact || !animator.HasState(_layerIndex, _stateHash)
            || float.IsNaN(strength) || float.IsInfinity(strength) || strength <= 0f) return false;

        float requestedWeight = Mathf.Clamp01(maximumWeight * strength);
        if (_reacting && _elapsed < minimumRetriggerInterval)
        {
            _strength = Mathf.Max(_strength, requestedWeight);
            return true;
        }

        _startWeight = CurrentWeight;
        _sampleStart = _sampleTime;
        _strength = requestedWeight;
        _elapsed = 0f;
        _reacting = true;
        // Motion time comes from the actor clock. Charge attacks can hold Animator.speed at zero,
        // so neither clip progression nor transition timing may depend on that global speed.
        animator.SetFloat(_timeParameterHash, _sampleTime);
        animator.Play(_stateHash, _layerIndex, 0f);
        return true;
    }

    [ContextMenu("Preview Hit Reaction (Play Mode)")]
    public void PreviewHit() => TriggerHit();

    private void Update()
    {
        if (!_reacting) return;
        if (!CanReact || animator.runtimeAnimatorController != _controller)
        {
            ClearReaction();
            return;
        }

        // Local combat hitstop freezes the sample and envelope together. Authored attack-speed
        // changes (including a held charge pose) do not change this presentation's duration.
        _elapsed += Mathf.Max(0f, owner.ActionDeltaTime);
        if (_elapsed >= duration)
        {
            ClearReaction();
            return;
        }
        float entering = blendIn > 0f ? Mathf.SmoothStep(0f, 1f, _elapsed / blendIn) : 1f;
        float leaving = blendOut > 0f ? Mathf.SmoothStep(0f, 1f, (duration - _elapsed) / blendOut) : 1f;
        // Rehits smoothly travel from the currently sampled pose to the new impulse.
        _sampleTime = Mathf.Lerp(_sampleStart, Mathf.Clamp01(_elapsed / duration), entering);
        animator.SetFloat(_timeParameterHash, _sampleTime);
        animator.SetLayerWeight(_layerIndex, Mathf.Lerp(_startWeight, _strength, entering) * leaving);
    }

    public void ClearReaction()
    {
        _reacting = false;
        _elapsed = _strength = _startWeight = _sampleTime = _sampleStart = 0f;
        if (animator != null && animator.runtimeAnimatorController != null
            && _layerIndex >= 0 && _layerIndex < animator.layerCount)
        {
            animator.SetLayerWeight(_layerIndex, 0f);
            if (_hasTimeParameter && animator.runtimeAnimatorController == _controller)
                animator.SetFloat(_timeParameterHash, 0f);
        }
    }
}
