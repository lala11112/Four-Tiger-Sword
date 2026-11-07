using System.Collections.Generic;
using UnityEngine;

/// <summary>Per-actor pattern cooldown and repetition memory. Assets are never modified.</summary>
public sealed class BossPatternSelector
{
    private readonly Dictionary<BossPatternSO, float> _readyAt = new Dictionary<BossPatternSO, float>();
    private readonly List<BossPatternOption> _options = new List<BossPatternOption>();
    private readonly List<float> _weights = new List<float>();
    private readonly List<float> _baselineWeights = new List<float>();
    private BossPatternSO _last, _previous;
    public BossPatternOption PositioningOption { get; private set; }
    public MonsterSkillData PositioningSkill { get; private set; }

    public BossPatternOption Select(Boss boss, BossCombatProfileSO profile, out string reason)
    {
        _options.Clear(); _weights.Clear(); _baselineWeights.Clear();
        PositioningOption = null; PositioningSkill = null;
        float bestPositioningWeight = -1f;
        double total = 0d;
        var context = BossCombatContext.Capture(boss);
        if (context.Target == null) { reason = "Combat target unavailable"; return null; }
        foreach (var option in profile.patterns)
        {
            if (option == null || option.pattern == null || !BossCombatValidation.Positive(option.weight)
                || option.condition == null
                || !BossCombatValidation.Range(option.condition.minimumDistance, option.condition.maximumDistance)) continue;
            if (_readyAt.TryGetValue(option.pattern, out float readyAt) && Time.time < readyAt) continue;
            bool conditionMatches = option.condition.Matches(context);
            if (!conditionMatches)
            {
                var adjusted = new BossCombatContext(context.Boss, context.Target,
                    Mathf.Clamp(context.Distance, option.condition.minimumDistance, option.condition.maximumDistance),
                    context.Angle, context.Visible, context.HpPercent);
                if (!option.condition.Matches(adjusted)) continue;
            }
            var first = FirstAttack(option.pattern, boss);
            if (first != null && !boss.IsSkillReady(first)) continue;
            float span = Mathf.Max(1f, option.condition.maximumDistance - option.condition.minimumDistance);
            float suitability = Mathf.Lerp(.25f, 1f, 1f - Mathf.Clamp01(Mathf.Abs(context.Distance - option.preferredDistance) / span));
            float baseline = option.weight * suitability;
            if (!BossCombatValidation.Positive(baseline)) continue;
            bool attackFirst = TryGetFirstCommand(option.pattern, boss, out var command)
                && command.Kind == BossPatternStepKind.Attack && command.Skill != null;
            bool attackInRange = !attackFirst
                || (context.Distance >= command.Skill.minimumRange && context.Distance <= command.Skill.executeRange);
            if (!conditionMatches || !attackInRange)
            {
                float minimum = option.condition.minimumDistance;
                float maximum = option.condition.maximumDistance;
                if (attackFirst)
                {
                    minimum = Mathf.Max(minimum, command.Skill.minimumRange);
                    maximum = Mathf.Min(maximum, command.Skill.executeRange);
                }
                if (!BossCombatValidation.Range(minimum, maximum)) continue;
                if (baseline > bestPositioningWeight)
                {
                    bestPositioningWeight = baseline;
                    PositioningOption = option;
                    PositioningSkill = attackFirst ? command.Skill : null;
                }
                continue;
            }
            float repeat = option.pattern == _last ? option.repeatMultiplier
                : option.pattern == _previous ? Mathf.Sqrt(option.repeatMultiplier) : 1f;
            float weight = baseline * repeat;
            if (!BossCombatValidation.Nonnegative(weight)) continue;
            _options.Add(option); _weights.Add(weight); _baselineWeights.Add(baseline); total += weight;
        }
        if (_options.Count == 0) { reason = "No ready pattern: conditions or cooldown; adjusting position"; return null; }
        bool repeatFallback = total <= 0d;
        if (repeatFallback)
        {
            for (int i = 0; i < _weights.Count; i++)
            { _weights[i] = _baselineWeights[i]; total += _weights[i]; }
        }
        double roll = Random.value * total;
        for (int i = 0; i < _options.Count; i++)
        {
            if (_weights[i] <= 0f) continue;
            roll -= _weights[i];
            if (roll > 0f && i + 1 < _options.Count) continue;
            var selected = _options[i];
            reason = (repeatFallback ? "Only repeat candidates remain; selected " : "Selected by conditions, range and repetition weight: ") + selected.pattern.name;
            return selected;
        }
        reason = "No ready pattern";
        return null;
    }

    public void RecordStarted(BossPatternOption option)
    {
        _readyAt[option.pattern] = Time.time + option.cooldown;
        _previous = _last; _last = option.pattern;
    }

    public void Reset()
    {
        _readyAt.Clear(); _last = _previous = null;
        PositioningOption = null; PositioningSkill = null;
    }

    private static bool TryGetFirstCommand(BossPatternSO pattern, Boss boss, out BossPatternStep command)
    {
        if (pattern is BossTacticalPatternSO tactical) return tactical.TryGetFirstCommand(boss, out command);
        if (pattern is BossSequencePatternSO sequence && sequence.steps != null)
            foreach (var step in sequence.steps)
            {
                if (step == null) continue;
                command = step.kind == BossPatternStepKind.Wait
                    ? BossPatternStep.Wait(step.waitSeconds) : BossPatternStep.Attack(step.skill);
                return true;
            }
        command = default;
        return false;
    }

    private static MonsterSkillData FirstAttack(BossPatternSO pattern, Boss boss)
    {
        if (pattern is BossTacticalPatternSO tactical)
        { tactical.TryGetFirstAttack(boss, out var skill); return skill; }
        if (pattern is BossSequencePatternSO sequence && sequence.steps != null)
            foreach (var step in sequence.steps)
                if (step != null && step.kind == BossPatternStepKind.Attack) return step.skill;
        return null;
    }
}
