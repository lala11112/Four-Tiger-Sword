using System;
using UnityEngine;

namespace ArpgHud
{
    [CreateAssetMenu(menuName="ARPG HUD/Element Skill Theme")]
    public sealed class HudElementTheme : ScriptableObject
    {
        [Serializable]
        public struct Element
        {
            public string Name;
            public Color Color;
            public Sprite E, R;
        }
        [SerializeField] Element[] elements = new Element[5];
        public void Configure(Element[] values) { elements=values; }
        public bool TryGet(int oneBasedIndex,out Element element)
        {
            element=default;
            if(elements==null || oneBasedIndex<1 || oneBasedIndex>elements.Length) return false;
            element=elements[oneBasedIndex-1];
            return element.E!=null && element.R!=null;
        }
    }
}
