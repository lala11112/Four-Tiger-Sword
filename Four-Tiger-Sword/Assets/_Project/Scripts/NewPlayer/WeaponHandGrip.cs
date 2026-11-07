using UnityEngine;

public class WeaponHandGrip : MonoBehaviour
{
    [SerializeField] private Transform[] fingers;
    [SerializeField] private Quaternion[] localRotations;
    [SerializeField] private GameObject[] weapons;

    private void LateUpdate() => ApplyGrip();

    public bool ApplyGrip()
    {
        if (weapons == null || fingers == null || localRotations == null) return false;

        bool holdingWeapon = false;
        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] == null || !weapons[i].activeInHierarchy) continue;
            holdingWeapon = true;
            break;
        }
        if (!holdingWeapon) return false;

        // Keep the sampled finger grip after animation without changing the hands or arms.
        bool applied = false;
        int count = Mathf.Min(fingers.Length, localRotations.Length);
        for (int i = 0; i < count; i++)
        {
            if (fingers[i] == null) continue;
            fingers[i].localRotation = localRotations[i];
            applied = true;
        }
        return applied;
    }
}
