using System;
using System.Collections.Generic;
using UnityEngine;

public enum BossTacticalStepKind { Attack = 0, Wait = 1, Move = 2, Recover = 3, Branch = 4 }

[Serializable]
public sealed class BossTacticalStep
{
    public BossTacticalStepKind kind;
    public MonsterSkillData skill;
    [Tooltip("Enabled를 켠 경우에만 보스 전용 조준·강인도 규칙을 적용합니다. 꺼져 있으면 기존 공격 동작을 유지합니다.")]
    public BossAttackPolicy attackPolicy;
    [Min(.01f)] public float timeout = 15f;
    [Min(0f)] public float seconds;
    public BossMovementSettings movement = new BossMovementSettings();
    public BossCombatCondition condition = new BossCombatCondition();
    [Tooltip("-2: 다음 순서, -1: 패턴 종료, 0 이상: 해당 단계 인덱스")]
    public int next = BossTacticalPatternSO.Next;
    [Tooltip("Branch 참일 때. -2: 다음 순서, -1: 종료, 0 이상: 단계 인덱스")]
    public int nextIfTrue = BossTacticalPatternSO.Next;
    [Tooltip("Branch 거짓일 때. -2: 다음 순서, -1: 종료, 0 이상: 단계 인덱스")]
    public int nextIfFalse = BossTacticalPatternSO.End;
}

/// <summary>Branches are evaluated only when the executor asks for its next completed-step boundary.</summary>
[CreateAssetMenu(fileName = "BossTacticalPattern", menuName = "SainGum/Monster/Boss Tactical Pattern")]
public sealed class BossTacticalPatternSO : BossPatternSO
{
    public const int Next = -2;
    public const int End = -1;

    public bool loop;
    [Min(1), Tooltip("분기와 실행 명령을 합한 한 번의 패턴 최대 단계 수. 순환 그래프도 이 한도에서 종료됩니다.")]
    public int maxCommandSteps = 128;
    public List<BossTacticalStep> steps = new List<BossTacticalStep>();

    public override IBossPattern CreateRuntime(Boss boss)
        => new TacticalRuntime(this, boss);

    /// <summary>Resolve opening branches without entering a runtime or skipping authored movement/waits.</summary>
    public bool TryGetFirstCommand(Boss boss, out BossPatternStep command)
    {
        command = default;
        if (steps == null || steps.Count == 0 || maxCommandSteps <= 0) return false;
        int index = 0;
        var context = BossCombatContext.Capture(boss);
        int limit = Mathf.Clamp(maxCommandSteps, 1, 4096);
        for (int budget = 0; budget < limit && index >= 0 && index < steps.Count; budget++)
        {
            var step = steps[index];
            if (step == null) return false;
            if (step.kind == BossTacticalStepKind.Branch)
            {
                int destination = step.condition != null && step.condition.Matches(context) ? step.nextIfTrue : step.nextIfFalse;
                index = ResolveNext(index, destination, steps.Count);
                continue;
            }
            switch (step.kind)
            {
                case BossTacticalStepKind.Attack:
                    command = BossPatternStep.Attack(step.skill, step.attackPolicy, step.timeout);
                    return true;
                case BossTacticalStepKind.Wait:
                    command = BossPatternStep.Wait(step.seconds);
                    return true;
                case BossTacticalStepKind.Move:
                    command = BossPatternStep.Move(step.movement);
                    return true;
                case BossTacticalStepKind.Recover:
                    command = BossPatternStep.Recover(step.seconds);
                    return true;
                default: return false;
            }
        }
        return false;
    }

    /// <summary>Preview the currently reachable first attack without creating or advancing a runtime.</summary>
    public bool TryGetFirstAttack(Boss boss, out MonsterSkillData skill)
    {
        skill = null;
        if (steps == null || steps.Count == 0 || maxCommandSteps <= 0) return false;
        int index = 0;
        var context = BossCombatContext.Capture(boss);
        int limit = Mathf.Clamp(maxCommandSteps, 1, 4096);
        for (int budget = 0; budget < limit && index >= 0 && index < steps.Count; budget++)
        {
            var step = steps[index];
            if (step == null || !Enum.IsDefined(typeof(BossTacticalStepKind), step.kind)) return false;
            if (step.kind == BossTacticalStepKind.Attack)
            {
                skill = step.skill;
                return skill != null;
            }
            int destination = step.kind == BossTacticalStepKind.Branch
                ? (step.condition != null && step.condition.Matches(context) ? step.nextIfTrue : step.nextIfFalse)
                : step.next;
            index = ResolveNext(index, destination, steps.Count);
        }
        return false;
    }

    public override IEnumerable<string> Validate()
    {
        if (maxCommandSteps <= 0 || maxCommandSteps > 4096) yield return "Max Command Steps must be between 1 and 4096.";
        if (steps == null || steps.Count == 0) { yield return "At least one tactical step is required."; yield break; }
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            string prefix = $"Step {i}: ";
            if (step == null) { yield return prefix + "Step is missing."; continue; }
            if (!Enum.IsDefined(typeof(BossTacticalStepKind), step.kind)) { yield return prefix + "Step kind is invalid."; continue; }
            if (step.kind == BossTacticalStepKind.Branch)
            {
                if (step.condition == null) yield return prefix + "Branch condition is missing.";
                else foreach (string issue in step.condition.Validate()) yield return prefix + issue;
                if (!ValidDestination(step.nextIfTrue, steps.Count)) yield return prefix + "True branch destination is invalid.";
                if (!ValidDestination(step.nextIfFalse, steps.Count)) yield return prefix + "False branch destination is invalid.";
            }
            else if (!ValidDestination(step.next, steps.Count)) yield return prefix + "Next destination is invalid.";

            switch (step.kind)
            {
                case BossTacticalStepKind.Attack:
                    if (step.skill == null) yield return prefix + "Attack skill is missing.";
                    else
                    {
                        if (!BossCombatValidation.Range(step.skill.minimumRange, step.skill.executeRange))
                            yield return prefix + "Attack execute range must be finite, nonnegative, and at least its minimum range.";
                        if (!BossCombatValidation.Nonnegative(step.skill.cooldown)) yield return prefix + "Attack cooldown must be finite and nonnegative.";
                        var issues = new HashSet<string>(BossAttackPolicy.ValidateSkill(step.skill));
                        if (step.attackPolicy != null)
                            foreach (string issue in step.attackPolicy.Validate(step.skill)) issues.Add(issue);
                        foreach (string issue in issues) yield return prefix + issue;
                    }
                    if (!BossCombatValidation.Positive(step.timeout)) yield return prefix + "Attack timeout must be finite and positive.";
                    break;
                case BossTacticalStepKind.Wait:
                case BossTacticalStepKind.Recover:
                    if (!BossCombatValidation.Nonnegative(step.seconds)) yield return prefix + "Duration must be finite and nonnegative.";
                    break;
                case BossTacticalStepKind.Move:
                    if (step.movement == null) yield return prefix + "Movement settings are missing.";
                    else foreach (string issue in step.movement.Validate()) yield return prefix + issue;
                    break;
            }
        }
        if (HasZeroTimeCycle()) yield return "A branch/wait/recover cycle can repeat without advancing time. Add a timed action or end the cycle.";
    }

    private bool HasZeroTimeCycle()
    {
        var zeroTime = new bool[steps.Count];
        var edges = new List<int>[steps.Count];
        var incoming = new int[steps.Count];
        int total = 0;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            zeroTime[i] = step != null && (step.kind == BossTacticalStepKind.Branch
                || ((step.kind == BossTacticalStepKind.Wait || step.kind == BossTacticalStepKind.Recover) && step.seconds == 0f));
            if (zeroTime[i]) total++;
        }
        for (int i = 0; i < steps.Count; i++)
        {
            if (!zeroTime[i]) continue;
            edges[i] = new List<int>(2);
            var step = steps[i];
            var destinations = step.kind == BossTacticalStepKind.Branch
                ? new[] { step.nextIfTrue, step.nextIfFalse } : new[] { step.next };
            foreach (int destination in destinations)
            {
                int next = ResolveNext(i, destination, steps.Count);
                if (next < 0 || !zeroTime[next]) continue;
                edges[i].Add(next);
                incoming[next]++;
            }
        }
        var queue = new Queue<int>();
        for (int i = 0; i < steps.Count; i++) if (zeroTime[i] && incoming[i] == 0) queue.Enqueue(i);
        int visited = 0;
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            visited++;
            foreach (int next in edges[current]) if (--incoming[next] == 0) queue.Enqueue(next);
        }
        return visited < total;
    }

    private static bool ValidDestination(int destination, int count)
        => destination == Next || destination == End || (destination >= 0 && destination < count);

    private static int ResolveNext(int current, int destination, int count)
    {
        if (!ValidDestination(destination, count)) return End;
        int resolved = destination == Next ? current + 1 : destination;
        return resolved < count ? resolved : End;
    }

    private sealed class TacticalRuntime : IBossPattern, IBossPatternProgress
    {
        private readonly BossTacticalPatternSO _asset;
        private readonly Boss _boss;
        private readonly BossTacticalStep[] _steps;
        private readonly int _limit;
        private int _index;
        private int _commands;
        private bool _reported;
        public bool Loop { get; }
        public int CurrentStepIndex { get; private set; } = End;

        public TacticalRuntime(BossTacticalPatternSO asset, Boss boss)
        {
            _asset = asset;
            _boss = boss;
            _steps = asset.steps != null ? asset.steps.ToArray() : Array.Empty<BossTacticalStep>();
            _limit = Mathf.Clamp(asset.maxCommandSteps, 1, 4096);
            foreach (string issue in asset.Validate())
            {
                _index = End;
                Report("Invalid tactical pattern. " + issue);
                _steps = Array.Empty<BossTacticalStep>();
                break;
            }
            Loop = asset.loop && _steps.Length > 0;
        }

        public void Enter() { _index = _steps.Length > 0 ? 0 : End; _commands = 0; CurrentStepIndex = End; }

        public bool TryGetNextStep(out BossPatternStep command)
        {
            command = default;
            while (_index >= 0 && _index < _steps.Length)
            {
                if (++_commands > _limit)
                {
                    _index = End;
                    Report("Tactical command limit reached; check the pattern for a cycle.");
                    return false;
                }
                int current = _index;
                CurrentStepIndex = current;
                var step = _steps[current];
                if (step == null) { _index = End; return false; }
                if (step.kind == BossTacticalStepKind.Branch)
                {
                    bool matches = step.condition != null && step.condition.Matches(BossCombatContext.Capture(_boss));
                    _index = ResolveNext(current, matches ? step.nextIfTrue : step.nextIfFalse, _steps.Length);
                    continue;
                }
                _index = ResolveNext(current, step.next, _steps.Length);
                switch (step.kind)
                {
                    case BossTacticalStepKind.Attack:
                        command = BossPatternStep.Attack(step.skill, step.attackPolicy, step.timeout);
                        return true;
                    case BossTacticalStepKind.Wait:
                        command = BossPatternStep.Wait(step.seconds);
                        return true;
                    case BossTacticalStepKind.Move:
                        command = BossPatternStep.Move(step.movement);
                        return true;
                    case BossTacticalStepKind.Recover:
                        command = BossPatternStep.Recover(step.seconds);
                        return true;
                    default:
                        _index = End;
                        Report("Unknown tactical step kind.");
                        return false;
                }
            }
            return false;
        }

        public void Exit() { _index = End; CurrentStepIndex = End; }

        private void Report(string message)
        {
            if (_reported) return;
            _reported = true;
            Debug.LogWarning($"{_asset.name}: {message}", _boss != null ? (UnityEngine.Object)_boss : _asset);
        }
    }
}
