/// <summary>Optional ground-attack capability; other forms retain press-to-attack.</summary>
public interface IHeavyAttackForm
{
    bool CanHeavyAttack { get; }
    float HeavyAttackHoldTime { get; }
    bool TryBeginHeavyAttack();
    bool TryContinueHeavyAttack();
    void UpdateHeavyAttack(out bool isComplete);
    void EndHeavyAttack();
}
