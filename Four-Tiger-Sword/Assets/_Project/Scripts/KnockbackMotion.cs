using UnityEngine;

/// <summary>Knockback power is initial speed (m/s), not damage or total distance.</summary>
public static class KnockbackMotion
{
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public static Vector3 Power(Vector3 direction, float speed)
    {
        if (!IsFinite(direction.sqrMagnitude) || !IsFinite(speed) || speed <= 0f) return Vector3.zero;
        return Vector3.ProjectOnPlane(direction, Vector3.up).normalized * speed;
    }

    public static Vector3 Velocity(Vector3 power, float resistance)
    {
        if (!IsFinite(power.sqrMagnitude) || !IsFinite(resistance)) return Vector3.zero;
        return Power(power, Mathf.Max(0f, power.magnitude - Mathf.Max(0f, resistance)));
    }

    public static Vector3 Displacement(Vector3 initialVelocity, float elapsed, float deltaTime, float duration)
    {
        if (!IsFinite(duration) || !IsFinite(elapsed) || !IsFinite(deltaTime)
            || !IsFinite(initialVelocity.sqrMagnitude) || duration <= 0f || deltaTime <= 0f) return Vector3.zero;
        float start = Mathf.Clamp(elapsed, 0f, duration);
        float end = Mathf.Clamp(elapsed + deltaTime, start, duration);
        // Exact integral of a linearly decaying velocity; clips the final frame.
        return initialVelocity * ((end - start) * (1f - (start + end) / (2f * duration)));
    }
}
