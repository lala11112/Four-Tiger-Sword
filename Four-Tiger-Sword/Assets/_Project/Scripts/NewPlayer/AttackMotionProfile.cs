using UnityEngine;

// A fixed cumulative table makes distance independent of frame partition and attack speed.
// Rebuilt once per action, with no per-frame curve integration or allocation.
public sealed class AttackMotionProfile
{
    private const int Samples = 128;
    private readonly float[] _distance = new float[Samples + 1];
    public float StartTime { get; private set; }
    public float EndTime { get; private set; }
    public float TotalDistance => _distance[Samples];

    public void Reset(WeaponActionData step, bool approach)
    {
        float duration = Mathf.Max(0f, step.Duration);
        EndTime = Mathf.Clamp(step.UseDistanceMovement ? step.MovementEndTime
            : approach ? step.ApproachEndTime : duration, 0f, duration);
        // Legacy actions reserve a short turn-in phase, then replay their integrated movement.
        StartTime = step.UseDistanceMovement ? Mathf.Clamp(step.MovementStartTime, 0f, EndTime)
            : Mathf.Min(0.08f, EndTime * 0.25f, Mathf.Max(0f, step.RotationEndTime));
        _distance[0] = 0f;
        if (EndTime <= StartTime)
        {
            System.Array.Clear(_distance, 0, _distance.Length);
            return;
        }
        if (step.UseDistanceMovement)
        {
            float total = Mathf.Max(0f, step.ForwardDistance);
            float initial = step.MovementProgress?.Evaluate(0f) ?? 0f;
            float span = (step.MovementProgress?.Evaluate(1f) ?? 1f) - initial;
            for (int i = 1; i <= Samples; i++)
            {
                float t = (float)i / Samples;
                float progress = span > 0.0001f
                    ? Mathf.Clamp01(((step.MovementProgress?.Evaluate(t) ?? t) - initial) / span) : t;
                _distance[i] = Mathf.Max(_distance[i - 1], total * progress);
            }
            _distance[Samples] = total;
        }
        else
        {
            // Preserve the old attack-speed-1 forward distance, including its approach cutoff.
            // Negative speeds are excluded: this profile represents forward movement only.
            float timeStep = EndTime / Samples;
            float previousSpeed = Mathf.Max(0f, (step.ThrustCurve?.Evaluate(0f) ?? 0f) * step.ThrustMultiplier);
            for (int i = 1; i <= Samples; i++)
            {
                float speed = Mathf.Max(0f, (step.ThrustCurve?.Evaluate(i * timeStep / duration) ?? 0f) * step.ThrustMultiplier);
                _distance[i] = _distance[i - 1] + (previousSpeed + speed) * 0.5f * timeStep;
                previousSpeed = speed;
            }
        }
    }

    public float DistanceAt(float time)
    {
        if (EndTime <= StartTime || time <= StartTime) return 0f;
        float sample = Mathf.Clamp01((time - StartTime) / (EndTime - StartTime)) * Samples;
        int index = Mathf.Min((int)sample, Samples - 1);
        return Mathf.Lerp(_distance[index], _distance[index + 1], sample - index);
    }

    public float Delta(float previousTime, float currentTime)
        => Mathf.Max(0f, DistanceAt(currentTime) - DistanceAt(previousTime));
}
