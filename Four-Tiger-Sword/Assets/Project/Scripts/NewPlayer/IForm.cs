public interface IForm
{
    void Equip(PlayerController playerController);
    void Unequip(PlayerController playerController);

    void BeginAttack();
    void UpdateAttack(out bool isComplete);
    void EndAttack();
}