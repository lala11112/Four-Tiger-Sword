using UnityEngine;
using UnityEngine.AI;

/// <summary>Finds a reachable position for an ordinary enemy without crossing the target.</summary>
internal sealed class EnemyTacticalPositioning
{
    private static readonly float[] CandidateAngles = { 0f, 30f, -30f, 60f, -60f };
    private const float DistanceBand = .5f;
    private const float RangeInset = .15f;
    private const float ChangeDirectionAdvantage = .35f;

    private readonly Enemy _enemy;
    private NavMeshPath _candidatePath = new();
    private NavMeshPath _bestPath = new();
    private Transform _target;
    private MonsterSkillData _skill;
    private Vector3 _direction;
    private Vector3 _destination;
    private Vector3 _plannedTargetPosition;
    private float _directionHoldUntil;
    private float _nextRepath;
    private bool _followingPath;

    public EnemyNavigation Navigation { get; }

    public EnemyTacticalPositioning(Enemy enemy)
    {
        _enemy = enemy;
        Navigation = new EnemyNavigation(enemy);
    }

    public MonsterSkillData PreferredSkill(Transform target)
    {
        var skills = _enemy.ActiveSkillSet?.skillData;
        if (skills == null) return null;
        float distance = Vector3.ProjectOnPlane(_enemy.transform.position - target.position, Vector3.up).magnitude;
        float closest = float.PositiveInfinity;
        MonsterSkillData best = null;
        foreach (var skill in skills)
        {
            if (skill == null || skill is MonsterSelfDestructSO || skill.weight <= 0f
                || skill.executeRange < 0f || skill.minimumRange > skill.executeRange
                || skill.engageRange < skill.minimumRange) continue;
            float difference = Mathf.Abs(distance - PreferredRange(skill));
            if (difference >= closest) continue;
            closest = difference;
            best = skill;
        }
        return best;
    }

    public void UpdateVisibleTarget(Transform target, MonsterSkillData skill, bool approach,
        float maxRange = float.PositiveInfinity)
    {
        if (!Navigation.Available || _enemy.IsKnockbacking) return;
        if (skill == null || maxRange < skill.minimumRange)
        {
            if (approach) Investigate(target.position);
            else Stop();
            return;
        }

        if (_target != target || _skill != skill)
        {
            Reset();
            _target = target;
            _skill = skill;
        }

        Vector3 offset = _enemy.transform.position - target.position;
        Vector3 away = Vector3.ProjectOnPlane(offset, Vector3.up);
        float maxDistance = Mathf.Min(maxRange, Mathf.Min(skill.executeRange, skill.engageRange));
        float preferredDistance = Mathf.Clamp(PreferredRange(skill), skill.minimumRange, maxDistance);
        float inset = Mathf.Min(RangeInset, Mathf.Max(0f, maxDistance - skill.minimumRange) * .25f);
        float desiredDistance = Mathf.Clamp(preferredDistance, skill.minimumRange + inset, maxDistance - inset);
        float desiredRadius = Mathf.Sqrt(Mathf.Max(0f, desiredDistance * desiredDistance - offset.y * offset.y));
        if (!approach && Mathf.Abs(away.magnitude - preferredDistance) <= DistanceBand
            && PositionAvailable(_enemy.transform.position))
        {
            Stop();
            _nextRepath = 0f;
            return;
        }

        float now = _enemy.ActionTime;
        bool pathBlocked = PathBlocked();
        if (now < _nextRepath && !pathBlocked) return;
        _nextRepath = now + _enemy.AISettings.destinationUpdateInterval;
        if (!pathBlocked && _followingPath && Navigation.Agent.hasPath
            && (_plannedTargetPosition - target.position).sqrMagnitude < .01f
            && PositionAvailable(_destination))
        {
            _enemy.CombatGroup?.ReservePosition(_enemy, _destination);
            return;
        }

        if (away.sqrMagnitude < .001f)
            away = Vector3.ProjectOnPlane(-_enemy.transform.forward, Vector3.up);
        away = away.sqrMagnitude > .001f ? away.normalized : Vector3.back;

        float bestLength = float.PositiveInfinity;
        Vector3 bestDirection = Vector3.zero;
        Vector3 bestPosition = Vector3.zero;
        if (Vector3.Dot(away, _direction) > 0f && TryCandidate(_direction, out var heldPosition, out float heldLength))
        {
            SaveCandidate(_direction, heldPosition, heldLength);
            if (!pathBlocked && now < _directionHoldUntil)
            {
                if (!FollowBest()) Stop();
                return;
            }
            bestLength -= ChangeDirectionAdvantage;
        }

        foreach (float angle in CandidateAngles)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * away;
            if (TryCandidate(direction, out var position, out float length) && length < bestLength)
                SaveCandidate(direction, position, length);
        }

        if (bestDirection != Vector3.zero && FollowBest()) return;
        // A narrow lane may have no free attack position. Wait clear of the enemy already there.
        Stop();

        bool TryCandidate(Vector3 direction, out Vector3 position, out float length)
        {
            Vector3 destination = target.position + direction * desiredRadius;
            destination.y = _enemy.transform.position.y;
            if (!Navigation.TryBuildPath(destination, false, target, _candidatePath, out position, out length)) return false;
            float distance = Vector3.Distance(position, target.position);
            return distance >= skill.minimumRange && distance <= maxDistance && PositionAvailable(position);
        }

        void SaveCandidate(Vector3 direction, Vector3 position, float length)
        {
            bestDirection = direction;
            bestPosition = position;
            bestLength = length;
            (_candidatePath, _bestPath) = (_bestPath, _candidatePath);
        }

        bool FollowBest()
        {
            if (!Navigation.Agent.SetPath(_bestPath)) return false;
            if (Vector3.Dot(_direction, bestDirection) < .999f)
                _directionHoldUntil = now + _enemy.AISettings.directionHoldDuration;
            _direction = bestDirection;
            _destination = bestPosition;
            _plannedTargetPosition = target.position;
            _followingPath = true;
            _enemy.CombatGroup?.ReservePosition(_enemy, bestPosition);
            Navigation.Animate(true);
            return true;
        }
    }

    public void Investigate(Vector3 position)
    {
        if (!Navigation.Available || _enemy.IsKnockbacking) return;
        if (_target != null || _skill != null) Reset();
        float now = _enemy.ActionTime;
        if (now < _nextRepath && !PathBlocked()) return;
        _nextRepath = now + _enemy.AISettings.destinationUpdateInterval;
        if (_followingPath && !PathBlocked() && Navigation.Agent.hasPath
            && (_plannedTargetPosition - position).sqrMagnitude < .01f) return;

        _plannedTargetPosition = position;
        position.y += Navigation.VerticalOffset;
        if ((_enemy.transform.position - position).sqrMagnitude < .0625f)
        {
            Stop();
            return;
        }
        _followingPath = Navigation.TryMove(position, false);
        // A target observed in the air may have no nearby NavMesh at the recorded height.
        if (!_followingPath && Mathf.Abs(position.y - _enemy.transform.position.y) > .5f)
        {
            position.y = _enemy.transform.position.y;
            _followingPath = Navigation.TryMove(position, false);
        }
        if (_followingPath) Navigation.Animate(true);
        else Stop();
    }

    public void HoldForAttack(Transform target)
    {
        Stop();
        FaceTarget(target);
        _nextRepath = 0f;
    }

    public void FaceTarget(Transform target)
    {
        Vector3 direction = Vector3.ProjectOnPlane(target.position - _enemy.transform.position, Vector3.up);
        if (direction.sqrMagnitude <= .001f) return;
        _enemy.transform.rotation = Quaternion.RotateTowards(_enemy.transform.rotation,
            Quaternion.LookRotation(direction), 360f * _enemy.ActionDeltaTime);
    }

    public void Reset()
    {
        Stop();
        _target = null;
        _skill = null;
        _direction = Vector3.zero;
        _directionHoldUntil = _nextRepath = 0f;
    }

    private void Stop()
    {
        Navigation.Stop();
        _enemy.CombatGroup?.ReleasePosition(_enemy);
        _followingPath = false;
    }

    private bool PathBlocked() => _followingPath && Navigation.Agent.hasPath && !Navigation.Agent.pathPending
        && Navigation.Agent.pathStatus != NavMeshPathStatus.PathComplete;

    private bool PositionAvailable(Vector3 position) => _enemy.CombatGroup?.IsPositionAvailable(_enemy, position) ?? true;

    private static float PreferredRange(MonsterSkillData skill) => skill.preferredRange > 0f ? skill.preferredRange : skill.executeRange;
}
