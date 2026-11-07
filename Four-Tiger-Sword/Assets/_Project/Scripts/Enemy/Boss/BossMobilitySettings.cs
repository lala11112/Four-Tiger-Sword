using System;
using UnityEngine;

/// <summary>Prefab-owned mobility tuning; shared combat profiles stay untouched.</summary>
[Serializable]
public sealed class BossMobilitySettings
{
    public bool enabled;
    [Min(1)] public int hitCount = 5;
    [Min(.01f)] public float hitWindow = 2f;
    [Min(.01f)] public float cornerDuration = .6f;
    [Min(0f)] public float cornerPlayerDistance = 4f;
    [Min(0f)] public float cornerEdgeDistance = 1.2f;
    [Min(.01f)] public float blockedRetreatDistance = 2f;
    [Min(0f)] public float farDistance = 8f;
    [Min(.01f)] public float farDuration = .5f;
    [Min(0f)] public float escapeCooldown = 6f;
    [Min(0f)] public float approachCooldown = 5f;
    [Min(0f)] public float sharedCooldown = 2f;
    [Min(.01f)] public float retryInterval = .5f;
    public BossMobilitySkillSO escapeJump;
    public BossMobilitySkillSO dashSlash;
    public BossMobilitySkillSO jumpSlam;
}
