using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Runtime identity for later spawn, exit, and boss encounter systems.</summary>
    [DisallowMultipleComponent]
    public sealed class BaekhoGeneratedRoom : MonoBehaviour
    {
        public int instanceId;
        public BaekhoRoomRole role;
    }
}
