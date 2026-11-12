using UnityEngine;

public partial class PlayerController
{
    [Header("Dash Afterimage")]
    [SerializeField, Tooltip("대시와 백스텝 중 현재 폼 색상의 잔상을 남깁니다.")]
    private bool _dashAfterimageEnabled = true;
    [SerializeField, Min(.02f), Tooltip("잔상 생성 간격 (행동 시간 기준 초)")]
    private float _dashAfterimageInterval = .05f;
    [SerializeField, Min(.01f), Tooltip("각 잔상이 사라지는 시간 (행동 시간 기준 초)")]
    private float _dashAfterimageLifetime = .22f;
    [SerializeField, Range(0f, 1f)] private float _dashAfterimageOpacity = .42f;

    private PlayerDashAfterimage _dashAfterimages;
    private Material _dashAfterimageMaterial;
    private float _dashAfterimageTimer;
    private bool _wasEmittingDashAfterimages;
    private bool _dashAfterimageShaderMissing;

    // Called after animation evaluation so each baked mesh captures this frame's pose.
    private void UpdateDashAfterimages(float deltaTime)
    {
        if (!_dashAfterimageEnabled || IsStageTransitioning || IsFallRecovering
            || (StatManager != null && StatManager.CurrentHp <= 0f))
        {
            _dashAfterimages?.Clear();
            _wasEmittingDashAfterimages = false;
            return;
        }

        bool emitting = StateMachine?.CurrentState is PlayerDashState dash && !dash.IsDashComplete;
        _dashAfterimages?.Advance(deltaTime);
        if (emitting && deltaTime > 0f)
        {
            _dashAfterimageTimer -= deltaTime;
            if (!_wasEmittingDashAfterimages || _dashAfterimageTimer <= 0f)
            {
                if (EnsureDashAfterimages())
                {
                    Color color = FinisherWheelGraphic.ElementColor(FinisherWheelSelection.IndexOf(Element));
                    color.a = Mathf.Clamp01(_dashAfterimageOpacity);
                    _dashAfterimages.Capture(color, Mathf.Max(.01f, _dashAfterimageLifetime));
                }
                // One capture per rendered frame: a hitch must not stack identical silhouettes.
                _dashAfterimageTimer = Mathf.Max(.02f, _dashAfterimageInterval);
            }
            _wasEmittingDashAfterimages = true;
        }
        else if (!emitting)
        {
            _wasEmittingDashAfterimages = false;
        }
        _dashAfterimages?.Draw();
    }

    private bool EnsureDashAfterimages()
    {
        if (_dashAfterimages != null) return true;
        if (_dashAfterimageShaderMissing || Animator == null) return false;
        // Resources retains the shader in player builds, even without a scene material reference.
        Shader shader = Resources.Load<Shader>("PlayerDashAfterimage");
        if (shader == null || !shader.isSupported)
        {
            _dashAfterimageShaderMissing = true;
            Debug.LogWarning("Player dash afterimage shader is missing or unsupported.", this);
            return false;
        }
        _dashAfterimageMaterial = new Material(shader)
        {
            name = "Player Dash Afterimage (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        _dashAfterimages = new PlayerDashAfterimage(this, _dashAfterimageMaterial);
        return true;
    }

    private void DisposeDashAfterimages()
    {
        _dashAfterimages?.Dispose();
        _dashAfterimages = null;
        if (_dashAfterimageMaterial != null)
        {
            if (Application.isPlaying) Destroy(_dashAfterimageMaterial);
            else DestroyImmediate(_dashAfterimageMaterial);
        }
        _dashAfterimageMaterial = null;
        _wasEmittingDashAfterimages = false;
        _dashAfterimageShaderMissing = false;
        _dashAfterimageTimer = 0f;
    }
}
