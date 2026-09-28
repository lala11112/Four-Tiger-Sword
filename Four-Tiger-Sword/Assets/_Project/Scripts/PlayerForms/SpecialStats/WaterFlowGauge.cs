using UnityEngine;

/// <summary>Three persistent segments; one complete segment pays for one heavy attack.</summary>
public class WaterFlowGauge
{
    public const int SegmentCount = 3;
    public float PointsPerSegment { get; }
    public float MaxGauge => PointsPerSegment * SegmentCount;
    public float CurrentGauge { get; private set; }
    public float GaugeRatio => CurrentGauge / MaxGauge;
    public int AvailableCharges => Mathf.FloorToInt(CurrentGauge / PointsPerSegment);

    public WaterFlowGauge(float pointsPerSegment = 100f)
    {
        PointsPerSegment = float.IsNaN(pointsPerSegment) || float.IsInfinity(pointsPerSegment)
            ? 100f : Mathf.Max(1f, pointsPerSegment);
    }

    public void Charge(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        CurrentGauge = Mathf.Min(CurrentGauge + amount, MaxGauge);
    }

    public bool TryConsumeCharge()
    {
        if (AvailableCharges < 1) return false;
        CurrentGauge -= PointsPerSegment;
        return true;
    }
}
