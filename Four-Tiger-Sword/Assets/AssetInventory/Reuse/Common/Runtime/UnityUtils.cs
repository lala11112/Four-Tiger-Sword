using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Unity-version-safe wrappers for Object.Find* APIs.
    /// Centralises all #if version guards so call sites stay clean.
    /// Supports Unity 2022.3 through 6.x on all graphics backends.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class UnityUtils
    {
        static readonly Dictionary<Type, TextMeshProWrappingAccessors> TextMeshProWrappingCache = new Dictionary<Type, TextMeshProWrappingAccessors>();

        /// <summary>
        /// Returns any single active object of type T in the loaded scenes.
        /// Uses FindAnyObjectByType on 2023.1+ (no ordering dependency) and
        /// falls back to FindObjectOfType on older versions.
        /// </summary>
        public static T FindAny<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        /// <summary>
        /// Returns any single active or inactive object of type T in the loaded scenes.
        /// </summary>
        public static T FindAnyIncludingInactive<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<T>(true);
#endif
        }

        /// <summary>
        /// Returns all active objects of type T in the loaded scenes.
        /// Uses the lowest-overhead, non-deprecated API for each Unity version:
        ///   2022.3        → FindObjectsOfType (legacy)
        ///   2023.1-6.3    → FindObjectsByType with FindObjectsSortMode.None
        ///   6.4+          → FindObjectsByType without a sort parameter
        /// </summary>
        public static T[] FindAll<T>() where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>();
#elif UNITY_2023_1_OR_NEWER
#pragma warning disable CS0618
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#pragma warning restore CS0618
#else
            return Object.FindObjectsOfType<T>();
#endif
        }

        /// <summary>
        /// Returns all active and inactive objects of type T in the loaded scenes.
        /// </summary>
        public static T[] FindAllIncludingInactive<T>() where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include);
#elif UNITY_2023_1_OR_NEWER
#pragma warning disable CS0618
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#pragma warning restore CS0618
#else
            return Object.FindObjectsOfType<T>(true);
#endif
        }

        /// <summary>
        /// Returns a stable integer identifier for the object.
        /// Uses GetEntityId on Unity 6.2+ and GetInstanceID on older versions.
        /// </summary>
        public static int GetStableId(this Object obj)
        {
#if UNITY_6000_2_OR_NEWER
            return obj.GetEntityId().GetHashCode();
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>
        /// Returns the realtime indirect-emission flag under its current Unity API name.
        /// </summary>
        public static MaterialGlobalIlluminationFlags RealtimeIndirectEmissionFlag
        {
            get
            {
#if UNITY_6000_7_OR_NEWER
                return MaterialGlobalIlluminationFlags.RealtimeIndirectEmission;
#else
                return MaterialGlobalIlluminationFlags.RealtimeEmissive;
#endif
            }
        }

        /// <summary>Returns the baked-emission flag under its current Unity API name.</summary>
        public static MaterialGlobalIlluminationFlags BakedEmissionFlag
        {
            get
            {
#if UNITY_6000_7_OR_NEWER
                return MaterialGlobalIlluminationFlags.BakedEmission;
#else
                return MaterialGlobalIlluminationFlags.BakedEmissive;
#endif
            }
        }

        /// <summary>Returns the Enlighten transport index while the native realtime-GI backend remains supported.</summary>
        public static int GetRealtimeLightmapIndex(this Renderer renderer)
        {
#pragma warning disable CS0618 // The existing Enlighten transport has no replacement binding API.
            return renderer.realtimeLightmapIndex;
#pragma warning restore CS0618
        }

        /// <summary>Sets the Enlighten transport index for fixture setup or restoration.</summary>
        public static void SetRealtimeLightmapIndex(this Renderer renderer, int index)
        {
#pragma warning disable CS0618 // Native qualification must control the actual Enlighten binding.
            renderer.realtimeLightmapIndex = index;
#pragma warning restore CS0618
        }

        /// <summary>Returns the native Enlighten UV scale and offset used by lighting diagnostics.</summary>
        public static Vector4 GetRealtimeLightmapScaleOffset(this Renderer renderer)
        {
#pragma warning disable CS0618 // Diagnostics must retain the actual Enlighten transport coordinates.
            return renderer.realtimeLightmapScaleOffset;
#pragma warning restore CS0618
        }

        /// <summary>Returns the native Enlighten vertex stream used by lighting diagnostics.</summary>
        public static Mesh GetRealtimeGIVertexStream(this MeshRenderer renderer)
        {
#pragma warning disable CS0618 // Diagnostics must retain the actual Enlighten transport stream.
            return renderer.enlightenVertexStream;
#pragma warning restore CS0618
        }

        /// <summary>Schedules native Enlighten albedo and emission extraction for the renderer.</summary>
        public static void UpdateRealtimeGIMaterials(this Renderer renderer)
        {
#pragma warning disable CS0618 // LED realtime indirect lighting still uses Unity's native Enlighten extraction.
            renderer.UpdateGIMaterials();
#pragma warning restore CS0618
        }

        /// <summary>
        /// Returns the assemblies loaded into the current Unity domain using the non-obsolete Unity API where available.
        /// </summary>
        public static IReadOnlyList<Assembly> GetLoadedAssemblies()
        {
#if UNITY_6000_6_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
#else
            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        /// <summary>
        /// Loads an assembly from disk using the non-obsolete Unity API where available.
        /// </summary>
        public static Assembly LoadAssemblyFromPath(string path)
        {
#if UNITY_6000_6_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.LoadFromPath(path);
#else
            return Assembly.LoadFrom(path);
#endif
        }

        /// <summary>
        /// Returns the filesystem path for a loaded assembly using the non-obsolete Unity API where available.
        /// </summary>
        public static string GetLoadedAssemblyPath(Assembly assembly)
        {
#if UNITY_6000_6_OR_NEWER
            return assembly.GetLoadedAssemblyPath();
#else
            return assembly.Location;
#endif
        }

        /// <summary>
        /// Sets TMP word wrapping through the newest available API without making Common depend on TextMeshPro.
        /// </summary>
        public static void SetTextMeshProWordWrapping(Component text, bool enabled)
        {
            if (text == null)
                return;

            GetTextMeshProWrappingAccessors(text.GetType()).Set(text, enabled);
        }

        /// <summary>
        /// Reads TMP word wrapping through the newest available API without making Common depend on TextMeshPro.
        /// </summary>
        public static bool GetTextMeshProWordWrapping(Component text, bool fallback = false)
        {
            if (text == null)
                return fallback;

            return GetTextMeshProWrappingAccessors(text.GetType()).Get(text, fallback);
        }

        static TextMeshProWrappingAccessors GetTextMeshProWrappingAccessors(Type type)
        {
            if (!TextMeshProWrappingCache.TryGetValue(type, out TextMeshProWrappingAccessors accessors))
            {
                accessors = new TextMeshProWrappingAccessors(type);
                TextMeshProWrappingCache.Add(type, accessors);
            }

            return accessors;
        }

        sealed class TextMeshProWrappingAccessors
        {
            readonly PropertyInfo _textWrappingModeProperty;
            readonly PropertyInfo _enableWordWrappingProperty;
            readonly object _normalMode;
            readonly object _noWrapMode;

            public TextMeshProWrappingAccessors(Type type)
            {
                _textWrappingModeProperty = FindWritableProperty(type, "textWrappingMode");
                _enableWordWrappingProperty = FindWritableProperty(type, "enableWordWrapping");

                if (_textWrappingModeProperty != null)
                {
                    _normalMode = ParseEnumValue(_textWrappingModeProperty.PropertyType, "Normal");
                    _noWrapMode = ParseEnumValue(_textWrappingModeProperty.PropertyType, "NoWrap");
                }
            }

            public void Set(Component text, bool enabled)
            {
                if (_textWrappingModeProperty != null && _normalMode != null && _noWrapMode != null)
                {
                    _textWrappingModeProperty.SetValue(text, enabled ? _normalMode : _noWrapMode, null);
                    return;
                }

                if (_enableWordWrappingProperty != null)
                    _enableWordWrappingProperty.SetValue(text, enabled, null);
            }

            public bool Get(Component text, bool fallback)
            {
                if (_textWrappingModeProperty != null && _noWrapMode != null && _textWrappingModeProperty.CanRead)
                {
                    object mode = _textWrappingModeProperty.GetValue(text, null);
                    return !Equals(mode, _noWrapMode);
                }

                if (_enableWordWrappingProperty != null && _enableWordWrappingProperty.CanRead)
                    return (bool)_enableWordWrappingProperty.GetValue(text, null);

                return fallback;
            }

            static PropertyInfo FindWritableProperty(Type type, string name)
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                return property != null && property.CanWrite ? property : null;
            }

            static object ParseEnumValue(Type enumType, string name)
            {
                try
                {
                    return Enum.Parse(enumType, name);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }
        }
    }
}
