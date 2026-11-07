using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BossSequenceStep
{
    public BossPatternStepKind kind;
    public MonsterSkillData skill;
    [Min(0f)] public float waitSeconds;
}

[CreateAssetMenu(fileName = "BossSequencePattern", menuName = "SainGum/Monster/Boss Sequence Pattern")]
public sealed class BossSequencePatternSO : BossPatternSO
{
    public bool loop = true;
    public List<BossSequenceStep> steps = new List<BossSequenceStep>();

    public override IBossPattern CreateRuntime(Boss boss)
    {
        var commands = new List<BossPatternStep>();
        if (steps != null)
            foreach (var step in steps)
            {
                if (step == null) continue;
                commands.Add(step.kind == BossPatternStepKind.Wait
                    ? BossPatternStep.Wait(step.waitSeconds) : BossPatternStep.Attack(step.skill));
            }
        return new SequenceRuntime(commands.ToArray(), loop);
    }

    private sealed class SequenceRuntime : IBossPattern
    {
        private readonly BossPatternStep[] _steps;
        private int _index;
        public bool Loop { get; }
        public SequenceRuntime(BossPatternStep[] steps, bool loop)
        { _steps = steps; Loop = loop && steps.Length > 0; }
        public void Enter() => _index = 0;
        public bool TryGetNextStep(out BossPatternStep step)
        {
            if (_index < _steps.Length) { step = _steps[_index++]; return true; }
            step = default;
            return false;
        }
        public void Exit() { }
    }
}