using System.Collections.Generic;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Tracks only the box visuals replaced by a generated cosmetic wall skin.</summary>
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class BaekhoWallAppearanceInstance : MonoBehaviour
    {
        [SerializeField, HideInInspector] private List<Renderer> originals = new List<Renderer>();
        [SerializeField, HideInInspector] private List<bool> enabledStates = new List<bool>();

        public void HideOriginals(Transform owner)
        {
            foreach (var renderer in owner.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.transform.IsChildOf(transform)) continue;
                if (!originals.Contains(renderer))
                {
                    originals.Add(renderer);
                    enabledStates.Add(renderer.enabled);
                }
                renderer.enabled = false;
            }
        }

        public void RestoreOriginals()
        {
            for (int i = 0; i < originals.Count; i++)
                if (originals[i]) originals[i].enabled = enabledStates[i];
            originals.Clear();
            enabledStates.Clear();
        }
    }
}
