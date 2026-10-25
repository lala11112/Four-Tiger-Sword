using UnityEngine;

/// <summary>The single source of truth for wheel preview and release selection.</summary>
public static class FinisherWheelSelection
{
    public const float InnerRadius = 90f;
    public const float OuterRadius = 250f;
    public const float SectorAngle = 72f;

    public static ElementType ElementAt(int index)
    {
        switch (index)
        {
            case 0: return ElementType.ELEMENT_FIRE;
            case 1: return ElementType.ELEMENT_EARTH;
            case 2: return ElementType.ELEMENT_GOLD;
            case 3: return ElementType.ELEMENT_WATER;
            case 4: return ElementType.ELEMENT_WOOD;
            default: return ElementType.ELEMENT_NONE;
        }
    }

    public static int IndexOf(ElementType element)
    {
        for (int i = 0; i < 5; i++)
            if (ElementAt(i) == element)
                return i;
        return -1;
    }

    /// <summary>
    /// Fire is centered at twelve o'clock; subsequent sectors run clockwise.
    /// The inner edge belongs to a sector and angular boundaries belong to the
    /// following clockwise sector. Positions outside the wheel retain direction.
    /// </summary>
    public static ElementType Resolve(Vector2 cursor, float innerRadius = InnerRadius)
    {
        if (float.IsNaN(cursor.x) || float.IsNaN(cursor.y)
            || float.IsInfinity(cursor.x) || float.IsInfinity(cursor.y))
            return ElementType.ELEMENT_NONE;

        float radius = Mathf.Max(0f, innerRadius);
        if (cursor == Vector2.zero || cursor.sqrMagnitude < radius * radius)
            return ElementType.ELEMENT_NONE;

        float clockwiseAngle = Mathf.Repeat(Mathf.Atan2(cursor.x, cursor.y) * Mathf.Rad2Deg, 360f);
        // Absorb float round-off when a point lies exactly on a radial boundary.
        int sector = Mathf.FloorToInt((clockwiseAngle + SectorAngle * 0.5f + 0.0001f) / SectorAngle) % 5;
        return ElementAt(sector);
    }
}
