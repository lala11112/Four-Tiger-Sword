public interface IForm
{
    void Equip(PlayerController playerController);
    void Unequip(PlayerController playerController);

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

    //히트박스그리는 기즈모임 나중에 필히 지울것
    void DrawHitboxGizmo();
}