using Unity.Cinemachine;
using UnityEngine;

public partial class PlayerController
{
    [Header("Incoming Hit Camera Shake")]
    [Min(0f), Tooltip("일반 적 공격에 실제 피해를 받았을 때의 화면 흔들림 강도")]
    public float IncomingHitShakeStrength = 0.15f;
    [Min(0f), Tooltip("다운 공격에 실제 피해를 받았을 때의 화면 흔들림 강도")]
    public float IncomingKnockdownShakeStrength = 0.25f;

    public void PlayIncomingHitCameraShake(object source, bool knockdown)
    {
        if (!(source is EnemyAction)) return;
        float strength = knockdown ? IncomingKnockdownShakeStrength : IncomingHitShakeStrength;
        if (!KnockbackMotion.IsFinite(strength) || strength <= 0f) return;
        if (ImpulseSource == null) ImpulseSource = GetComponent<CinemachineImpulseSource>();
        if (ImpulseSource == null) return;

        Vector3 velocity = ImpulseSource.DefaultVelocity * strength;
        if (!KnockbackMotion.IsFinite(velocity.sqrMagnitude) || velocity.sqrMagnitude <= 0f) return;
        ImpulseSource.GenerateImpulseWithVelocity(velocity);
        CombatFeedbackTelemetry.Emit("player-hit-shake", gameObject, gameObject,
            detail: knockdown ? "knockdown" : "hit", value: strength, position: transform.position);
    }
}
