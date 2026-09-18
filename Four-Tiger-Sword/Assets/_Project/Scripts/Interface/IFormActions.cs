public interface IFormActions
{
    void BeginAttack();
    void UpdateAttack(out bool isComplete);
    void EndAttack();

    void BeginAirAttack();
    void UpdateAirAttackStart(out bool isComplete);
    void BeginAirAttackLoop();
    void BeginAirAttackFinish();
    void UpdateAirAttack(out bool isComplete);
    void EndAirAttack();

    void BeginParry();
    void EndParry();

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
