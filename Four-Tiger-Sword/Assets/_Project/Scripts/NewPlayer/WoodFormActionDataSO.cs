using UnityEngine;

[CreateAssetMenu(fileName = "WoodFormAction", menuName = "Combat/Forms/Wood")]
public class WoodFormActionDataSO : WeaponActionDataSO
{
    [Header("Wood Field")]
    [Min(0.1f)] public float AreaRadius = 4f;
    [Min(0.1f)] public float FieldDuration = 5f;
    [Range(0f, 1f)] public float HealMaxHpPerSecond = 0.05f;
}
