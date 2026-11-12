using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class PlayerController
{
    [Header("Fall Recovery")]
    [SerializeField, Range(0f, 1f)] private float _fallHealthCost = .1f;
    [SerializeField, Min(0f)] private float _fallVoidMargin = .5f;
    [SerializeField, Min(0f), Tooltip("데드존 접촉 후 계속 낙하하는 시간 (실제 초)")]
    private float _fallDropDelaySeconds = 1f;
    [SerializeField, Min(0f)] private float _fallFadeOutSeconds = .15f;
    [SerializeField, Min(0f)] private float _fallFadeInSeconds = .2f;
    [SerializeField, Min(0f), Tooltip("화면 복구 후 빈 복귀 장소를 보여주는 시간 (실제 초)")]
    private float _fallEmptyHoldSeconds = .25f;
    [SerializeField, Min(0f)] private float _fallProtectionSeconds = 1f;

    public bool IsFallRecovering => _fallPhase != FallRecoveryPhase.None;
    public bool IsFallProtected => IsFallRecovering || _fallProtectionRemaining > 0f;

    private const double FallLandingRetrySeconds = .75;
    private enum FallRecoveryPhase { None, DropDelay, WaitingForLanding, FadingOut, Covered, FadingIn, EmptyHold, Revealing }

    private DungeonFallRecovery _fallRecovery;
    private PlayerFallReveal _fallReveal;
    private FallRecoveryPhase _fallPhase;
    private float _fallFadeElapsed, _fallPhaseElapsed, _fallProtectionRemaining;
    private double _fallLandingDeadline;
    private GameObject _fallCoverRoot;
    private UnityEngine.UI.Image _fallCover;
    private readonly List<CinemachineInputAxisController> _fallCameraInputs = new();

    internal void BindFallRecovery(DungeonFallRecovery recovery)
    {
        if (_fallRecovery == recovery) return;
        CancelFallRecovery();
        _fallRecovery = recovery;
        if (recovery != null)
            _fallReveal = GetComponent<PlayerFallReveal>() ?? gameObject.AddComponent<PlayerFallReveal>();
    }

    internal void UnbindFallRecovery(DungeonFallRecovery recovery)
    {
        if (_fallRecovery == recovery) BindFallRecovery(null);
    }

    private bool UpdateFallRecovery()
    {
        if (IsFallRecovering)
        {
            if (_fallRecovery == null || !_fallRecovery.IsValid || StatManager == null || StatManager.CurrentHp <= 0f)
            {
                CancelFallRecovery();
                return false;
            }

            AdvanceFallRecovery();
            return true;
        }

        if (IsGameplayModalPaused || StatManager == null || StatManager.CurrentHp <= 0f
            || Controller == null || !Controller.enabled) return false;
        _fallProtectionRemaining = Mathf.Max(0f, _fallProtectionRemaining - Time.unscaledDeltaTime);
        if (_fallRecovery == null || !_fallRecovery.IsValid || Time.deltaTime <= 0f) return false;
        if (!_fallRecovery.HasEnteredDeadZone() && !_fallRecovery.IsBelowVoid(_fallVoidMargin))
        {
            _fallRecovery.TickSafePosition(Time.deltaTime);
            return false;
        }

        BeginFallRecovery();
        return true;
    }

    private void BeginFallRecovery()
    {
        CancelFinisherSelection();
        _fallPhase = FallRecoveryPhase.DropDelay;
        _fallPhaseElapsed = 0f;
        _fallProtectionRemaining = 0f;
        Input?.SetFallRecoveryInputBlocked(true);
        float fallingVelocity = KnockbackMotion.IsFinite(VerticalVelocity) ? Mathf.Min(-2f, VerticalVelocity) : -2f;

        StopParryKnockback();
        CancelPendingSwitchAttack();
        ConsumeHitReaction();
        // Action exits restore collisions and visibility. Keep them from moving the controller during cleanup.
        Controller.enabled = false;
        try { StateMachine?.ChangeState(new FallRecoveryState(this)); }
        finally { Controller.enabled = true; }
        VerticalVelocity = fallingVelocity;
        _coyoteTimer = 0f;
        if (Animator != null) Animator.applyRootMotion = false;
        SetAnimationSpeed(1f);
        CancelHitStops();
        StopAttackTrails(true);
        _combatCamera?.Cancel(this, true);

        foreach (var camera in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!TracksPlayer(camera)) continue;
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>())
            {
                if (!input.enabled) continue;
                _fallCameraInputs.Add(input);
                input.enabled = false;
            }
        }

    }

    private void AdvanceFallRecovery()
    {
        switch (_fallPhase)
        {
            case FallRecoveryPhase.DropDelay:
                float duration = SafeFallDuration(_fallDropDelaySeconds);
                float delta = Mathf.Min(Time.unscaledDeltaTime, Mathf.Max(0f, duration - _fallPhaseElapsed));
                // Recovery owns input/action time; only vertical gravity continues during this delay.
                if (Controller != null && Controller.enabled && delta > 0f)
                {
                    VerticalVelocity += Gravity * delta;
                    Controller.Move(Vector3.up * (VerticalVelocity * delta));
                }
                _fallPhaseElapsed += Time.unscaledDeltaTime;
                if (_fallPhaseElapsed < duration) break;
                VerticalVelocity = 0f;
                SetAnimationSpeed(0f);
                _fallLandingDeadline = Time.realtimeSinceStartupAsDouble + FallLandingRetrySeconds;
                _fallPhase = FallRecoveryPhase.WaitingForLanding;
                break;
            case FallRecoveryPhase.WaitingForLanding:
                if (_fallRecovery.TryGetRecoveryPose(out _, out _))
                {
                    ShowFallCover();
                    _fallFadeElapsed = 0f;
                    _fallPhase = FallRecoveryPhase.FadingOut;
                }
                else WaitForFallLanding();
                break;
            case FallRecoveryPhase.FadingOut:
                if (AdvanceFallCover(_fallFadeOutSeconds, false)) _fallPhase = FallRecoveryPhase.Covered;
                break;
            case FallRecoveryPhase.Covered:
                // Recheck under black because another actor may have moved onto the landing spot.
                if (!_fallRecovery.TryGetRecoveryPose(out var position, out var rotation))
                {
                    HideFallCover();
                    WaitForFallLanding();
                    break;
                }

                string revealError = null;
                if (_fallReveal == null || !_fallReveal.Hide(out revealError))
                {
                    FailFallRecovery(_fallReveal == null ? "플레이어 복귀 연출을 찾지 못했습니다." : revealError);
                    break;
                }
                WarpPlayerAndCamera(position, rotation);
                _fallRecovery.NotifyWarp();
                SetAnimationSpeed(1f);
                StateMachine?.ChangeState(new PlayerIdleState(this));
                _fallFadeElapsed = 0f;
                _fallPhase = FallRecoveryPhase.FadingIn;
                StatManager.ConsumeHealth(StatManager.MaxHp * _fallHealthCost);
                break;
            case FallRecoveryPhase.FadingIn:
                if (!AdvanceFallCover(_fallFadeInSeconds, true)) break;
                HideFallCover();
                _fallPhaseElapsed = 0f;
                _fallPhase = FallRecoveryPhase.EmptyHold;
                break;
            case FallRecoveryPhase.EmptyHold:
                _fallPhaseElapsed += Time.unscaledDeltaTime;
                if (_fallPhaseElapsed < SafeFallDuration(_fallEmptyHoldSeconds)) break;
                _fallReveal.BeginReveal();
                _fallPhase = FallRecoveryPhase.Revealing;
                break;
            case FallRecoveryPhase.Revealing:
                if (_fallReveal.Advance(Time.unscaledDeltaTime))
                    EndFallRecovery(SafeFallDuration(_fallProtectionSeconds));
                break;
        }
    }

    private void WaitForFallLanding()
    {
        if (Time.realtimeSinceStartupAsDouble < _fallLandingDeadline)
        {
            _fallPhase = FallRecoveryPhase.WaitingForLanding;
            return;
        }

        FailFallRecovery("추락 후 돌아갈 수 있는 안전한 위치를 찾지 못했습니다.");
    }

    private void FailFallRecovery(string reason)
    {
        var recovery = _fallRecovery;
        CancelFallRecovery();
        recovery?.ReportFailure(reason);
    }

    private static float SafeFallDuration(float seconds) =>
        KnockbackMotion.IsFinite(seconds) ? Mathf.Max(0f, seconds) : 0f;

    private bool AdvanceFallCover(float duration, bool reveal)
    {
        _fallFadeElapsed += Time.unscaledDeltaTime;
        float progress = duration > 0f && KnockbackMotion.IsFinite(duration)
            ? Mathf.Clamp01(_fallFadeElapsed / duration) : 1f;
        SetFallCoverAlpha(Mathf.SmoothStep(reveal ? 1f : 0f, reveal ? 0f : 1f, progress));
        return progress >= 1f;
    }

    private void ShowFallCover()
    {
        _fallCoverRoot = new GameObject("FallRecoveryCover", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
        var scene = gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && _fallCoverRoot.scene != scene)
            SceneManager.MoveGameObjectToScene(_fallCoverRoot, scene);
        var canvas = _fallCoverRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 240;

        _fallCover = new GameObject("Black", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
        var rect = _fallCover.rectTransform;
        rect.SetParent(_fallCoverRoot.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        SetFallCoverAlpha(0f);
    }

    private void SetFallCoverAlpha(float alpha)
    {
        if (_fallCover != null) _fallCover.color = new Color(0f, 0f, 0f, alpha);
    }

    private void HideFallCover()
    {
        if (_fallCoverRoot != null)
        {
            _fallCoverRoot.SetActive(false);
            Destroy(_fallCoverRoot);
        }
        _fallCoverRoot = null;
        _fallCover = null;
    }

    private void CancelFallRecovery() => EndFallRecovery(0f);

    private void EndFallRecovery(float protectionSeconds)
    {
        _fallProtectionRemaining = protectionSeconds;
        _fallReveal?.Restore();
        if (!IsFallRecovering) return;
        _fallPhase = FallRecoveryPhase.None;
        HideFallCover();
        Input?.SetFallRecoveryInputBlocked(false);
        if (StatManager != null && StatManager.CurrentHp > 0f && StateMachine?.CurrentState is FallRecoveryState)
        {
            SetAnimationSpeed(1f);
            StateMachine.ChangeState(new PlayerIdleState(this));
        }
        foreach (var input in _fallCameraInputs)
            if (input != null) input.enabled = true;
        _fallCameraInputs.Clear();
    }

    private sealed class FallRecoveryState : IPlayerState
    {
        private readonly PlayerController _player;
        public FallRecoveryState(PlayerController player) => _player = player;
        public void Enter() => new PlayerFallState(_player).Enter();
        public void Update() { }
        public void Exit() { }
    }
}
