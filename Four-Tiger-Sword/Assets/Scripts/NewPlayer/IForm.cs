public interface IForm
{
    void Equip(PlayerController playerController);
    void Unequip(PlayerController playerController);

    void BeginAttack();
    void UpdateAttack(out bool isComplete);
    void EndAttack();

    //히트박스그리는 기즈모임 나중에 필히 지울것
    void DrawHitboxGizmo();
}