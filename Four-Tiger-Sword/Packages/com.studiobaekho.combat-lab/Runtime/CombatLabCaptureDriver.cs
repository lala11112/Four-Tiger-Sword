#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

namespace StudioBaekho.CombatLab
{
    /// <summary>End-of-frame capture continues during Time.timeScale = 0 hitstop.</summary>
    public sealed class CombatLabCaptureDriver : MonoBehaviour
    {
        internal Action FrameReady;
        IEnumerator Start()
        {
            var wait = new WaitForEndOfFrame();
            while (true) { yield return wait; FrameReady?.Invoke(); }
        }
    }
}
#endif
