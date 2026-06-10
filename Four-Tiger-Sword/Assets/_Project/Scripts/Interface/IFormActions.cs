public interface IFormActions
{
    void BeginAttack();
    void UpdateAttack(out bool isComplete);
    void EndAttack();

    void BeginAirAttack();
    void UpdateAirAttack(out bool isComplete);
    void EndAirAttack();

    void BeginSkill();
    void UpdateSkill(out bool isComplete);
    void EndSkill();

    void BeginUltimate();
    void UpdateUltimate(out bool isComplete);
    void EndUltimate();

    void BeginCounter();
    void UpdateCounter(out bool isComplete);
    void EndCounter();
}
