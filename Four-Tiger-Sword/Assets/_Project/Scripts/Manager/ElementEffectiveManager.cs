using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ElementEffective
{
    public ElementType AttackType;
    public ElementType DefenseType;
    public float multiplier;
}

[CreateAssetMenu(fileName = "ElementEffectiveManager", menuName = "ElementEffectiveManager")]
public class ElementEffectiveManager : ScriptableObject
{
    public List<ElementEffective> elementEffectiveList;
    private Dictionary<(ElementType, ElementType), float> elementEffectiveDict;

    public void Initialize()
    {
        elementEffectiveDict = new Dictionary<(ElementType, ElementType), float>();
        foreach (var item in elementEffectiveList)
        {
            elementEffectiveDict.Add((item.AttackType, item.DefenseType), item.multiplier);
        }
    }

    public float GetElementEffective(ElementType attackType, ElementType defenseType)
    {
        if (elementEffectiveDict.TryGetValue((attackType, defenseType), out float multiplier))
        {
            return multiplier;
        }
        return 1f;
    }
}
