using System;

/// <summary>Stable per-spawn reward, independent of combat, UI, and Unity's shared RNG.</summary>
public static class EnemyGoldRoll
{
    public static int Roll(int stageSeed, int roomId, int waveIndex, int spawnIndex, int minimum, int maximum)
    {
        if (minimum < 0 || maximum < minimum || maximum == int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)stageSeed) * 16777619u;
            hash = (hash ^ (uint)roomId) * 16777619u;
            hash = (hash ^ (uint)waveIndex) * 16777619u;
            hash = (hash ^ (uint)spawnIndex) * 16777619u;
            hash = (hash ^ 0x474F4C44u) * 16777619u;
            return new Random((int)hash).Next(minimum, maximum + 1);
        }
    }
}
