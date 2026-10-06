using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Compatibility bridge for Unity's editor-only audio preview service, which has no public API.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class CommonEditorAudioPreview
    {
        static MethodInfo _playMethod;
        static MethodInfo _stopAllMethod;
        static bool _initialized;

        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return _playMethod != null && _stopAllMethod != null;
            }
        }

        public static bool Play(AudioClip clip)
        {
            if (clip == null || !IsAvailable)
                return false;

            _playMethod.Invoke(null, new object[] { clip, 0, false });
            return true;
        }

        public static void StopAll()
        {
            if (!IsAvailable)
                return;

            _stopAllMethod.Invoke(null, Array.Empty<object>());
        }

        static void EnsureInitialized()
        {
            if (_initialized)
                return;

            Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtilType != null)
            {
                _playMethod = FindMethod(
                    audioUtilType,
                    "PlayPreviewClip",
                    "PlayClip",
                    new[] { typeof(AudioClip), typeof(int), typeof(bool) });
                _stopAllMethod = FindMethod(
                    audioUtilType,
                    "StopAllPreviewClips",
                    "StopAllClips",
                    Type.EmptyTypes);
            }

            _initialized = true;
        }

        static MethodInfo FindMethod(Type owner, string currentName, string legacyName, Type[] parameters)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
            MethodInfo method = owner.GetMethod(currentName, flags, null, parameters, null);
            return method ?? owner.GetMethod(legacyName, flags, null, parameters, null);
        }
    }
}
