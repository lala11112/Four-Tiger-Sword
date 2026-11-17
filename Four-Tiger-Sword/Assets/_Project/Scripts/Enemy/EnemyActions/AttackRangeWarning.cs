using UnityEngine;

public enum AttackRangeWarningKind { Circle, Rectangle, Corridor }

/// <summary>World-space ground footprint for an upcoming hit, before the renderer's floor lift.</summary>
public readonly struct AttackRangeWarning
{
    public readonly AttackRangeWarningKind Kind;
    public readonly Vector3 Center;
    public readonly Quaternion Rotation;
    public readonly Vector2 Size;
    public readonly float Radius;
    public readonly float Progress;

    private AttackRangeWarning(AttackRangeWarningKind kind, Vector3 center, Quaternion rotation,
        Vector2 size, float radius, float progress)
    {
        Kind = kind;
        Center = center;
        Rotation = rotation;
        Size = size;
        Radius = radius;
        Progress = Mathf.Clamp01(progress);
    }

    public static AttackRangeWarning Circle(Vector3 center, float radius, float progress)
        => new(AttackRangeWarningKind.Circle, center, Quaternion.identity,
            Vector2.one * (2f * radius), radius, progress);

    public static AttackRangeWarning Box(Vector3 center, Quaternion rotation, Vector2 size, float progress)
        => new(AttackRangeWarningKind.Rectangle, center, rotation, size, 0f, progress);

    public static AttackRangeWarning Corridor(Vector3 start, Vector3 end, float radius, float progress)
    {
        Vector3 direction = end - start;
        direction.y = 0f;
        Quaternion rotation = direction.sqrMagnitude > .000001f
            ? Quaternion.LookRotation(direction) : Quaternion.identity;
        float diameter = 2f * radius;
        return new AttackRangeWarning(AttackRangeWarningKind.Corridor, (start + end) * .5f, rotation,
            new Vector2(diameter, direction.magnitude + diameter), radius, progress);
    }
}
