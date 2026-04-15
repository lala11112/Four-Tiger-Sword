public interface IFormCooldown
{
    bool CanSkill    { get; }
    bool CanUltimate { get; }
    void UpdateCooldowns();
}
