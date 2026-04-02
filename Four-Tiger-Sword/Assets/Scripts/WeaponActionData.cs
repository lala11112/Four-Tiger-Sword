using UnityEngine;

[CreateAssetMenu(fileName = "WeaponActionData", menuName = "ScriptableObject/WeaponActionData")]
public class WeaponActionData : ScriptableObject
{
    //public string? int? form; //현재 폼 (추후 변경 가능)
    public float frame; //선딜레이
    public Vector3 hitbox; // 히트박스 크기 (추후 변경 가능)
    public int damage; //데미지
    public int combo; //콤보
}
