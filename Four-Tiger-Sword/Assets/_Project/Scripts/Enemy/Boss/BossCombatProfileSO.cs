using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BossPatternOption
{
    public BossPatternSO pattern;
    public BossCombatCondition condition = new BossCombatCondition();
    [Min(0f)] public float weight = 1f;
    [Min(0f)] public float cooldown = 2f;
    [Min(0f)] public float recovery = .5f;
    [Range(0f, 1f)] public float repeatMultiplier = .25f;
    [Min(0f)] public float preferredDistance = 3f;
}

[CreateAssetMenu(fileName = "BossCombatProfile", menuName = "SainGum/Monster/Boss Combat Profile")]
public sealed class BossCombatProfileSO : ScriptableObject
{
    [Header("판단 및 선호 거리")]
    [Min(.01f)] public float decisionInterval = .2f;
    [Min(0f)] public float preferredMinDistance = 2f;
    [Min(0f)] public float preferredMaxDistance = 4f;
    [Min(0f)] public float turnSpeed = 240f;

    [Header("전투 이동")]
    [Min(.01f)] public float movementTimeout = 6f;
    [Min(.01f)] public float stuckTimeout = 1.5f;
    [Min(.01f)] public float repathInterval = .2f;
    [Min(0f)] public float strafeDuration = 1f;
    [Min(0f), Tooltip("패턴 후 회복이 끝나면 다음 공격 전에 거리를 조절하며 이동하는 시간. 0이면 바로 다음 패턴을 선택합니다.")]
    public float repositionDuration;

    [Header("중단 후 빈틈")]
    [Min(0f)] public float failureRecovery = .25f;
    [Min(0f)] public float parryRecovery = 1f;

    [Header("상황별 패턴")]
    [Tooltip("이 프로필은 각 패턴을 한 순회 실행한 뒤 쿨타임·조건으로 다시 선택합니다. 기존 패턴 에셋의 Loop 값은 프로필 실행에 적용하지 않습니다.")]
    public List<BossPatternOption> patterns = new List<BossPatternOption>();

    public IEnumerable<string> Validate()
    {
        if (!BossCombatValidation.Positive(decisionInterval)) yield return "Decision Interval must be finite and positive.";
        if (!BossCombatValidation.Range(preferredMinDistance, preferredMaxDistance)) yield return "Preferred distance must be a finite, nonnegative ordered range.";
        if (!BossCombatValidation.Nonnegative(turnSpeed)) yield return "Turn Speed must be finite and nonnegative.";
        if (!BossCombatValidation.Positive(movementTimeout)) yield return "Movement Timeout must be finite and positive.";
        if (!BossCombatValidation.Positive(stuckTimeout)) yield return "Stuck Timeout must be finite and positive.";
        if (!BossCombatValidation.Positive(repathInterval)) yield return "Repath Interval must be finite and positive.";
        if (!BossCombatValidation.Nonnegative(strafeDuration)) yield return "Strafe Duration must be finite and nonnegative.";
        if (!BossCombatValidation.Nonnegative(repositionDuration)) yield return "Reposition Duration must be finite and nonnegative.";
        if (!BossCombatValidation.Nonnegative(failureRecovery)) yield return "Failure Recovery must be finite and nonnegative.";
        if (!BossCombatValidation.Nonnegative(parryRecovery)) yield return "Parry Recovery must be finite and nonnegative.";
        if (patterns == null || patterns.Count == 0)
        {
            yield return "At least one pattern option is required.";
            yield break;
        }

        bool hasPositiveWeight = false;
        for (int i = 0; i < patterns.Count; i++)
        {
            var option = patterns[i];
            string prefix = $"Pattern {i}: ";
            if (option == null) { yield return prefix + "Option is missing."; continue; }
            if (option.pattern == null) yield return prefix + "Pattern asset is missing.";
            else foreach (string issue in option.pattern.Validate()) yield return prefix + issue;
            if (option.condition == null) yield return prefix + "Condition is missing.";
            else foreach (string issue in option.condition.Validate()) yield return prefix + issue;
            if (!BossCombatValidation.Nonnegative(option.weight)) yield return prefix + "Weight must be finite and nonnegative.";
            else hasPositiveWeight |= option.weight > 0f;
            if (!BossCombatValidation.Nonnegative(option.cooldown)) yield return prefix + "Cooldown must be finite and nonnegative.";
            if (!BossCombatValidation.Nonnegative(option.recovery)) yield return prefix + "Recovery must be finite and nonnegative.";
            if (!BossCombatValidation.Unit(option.repeatMultiplier)) yield return prefix + "Repeat Multiplier must be between 0 and 1.";
            if (!BossCombatValidation.Nonnegative(option.preferredDistance)) yield return prefix + "Preferred Distance must be finite and nonnegative.";
        }
        if (!hasPositiveWeight) yield return "At least one pattern option must have a positive weight.";
    }
}

internal static class BossCombatValidation
{
    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool Nonnegative(float value) => Finite(value) && value >= 0f;
    public static bool Positive(float value) => Finite(value) && value > 0f;
    public static bool Unit(float value) => Finite(value) && value >= 0f && value <= 1f;
    public static bool Range(float minimum, float maximum)
        => Nonnegative(minimum) && Nonnegative(maximum) && minimum <= maximum;
}
