using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    public GameObject FireWeapon;
    public GameObject WaterWeapon;
    public GameObject WaterOffhandWeapon;

    public void SetForm(ElementType element)
    {
        FireWeapon?.SetActive(element == ElementType.ELEMENT_FIRE);
        WaterWeapon?.SetActive(element == ElementType.ELEMENT_WATER);
        WaterOffhandWeapon?.SetActive(element == ElementType.ELEMENT_WATER);
    }
}
