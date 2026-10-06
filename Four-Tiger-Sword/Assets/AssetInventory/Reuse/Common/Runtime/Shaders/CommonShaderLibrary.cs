using UnityEngine;

namespace ImpossibleRobert.Common
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.AutoStaticsCleanup]
#endif
    public static partial class CommonShaderLibrary
    {
        public const string SampleLitShaderName = "Impossible Robert/Common/Sample Lit";

        const string UniversalLitShaderName = "Universal Render Pipeline/Lit";
        const string HdrpLitShaderName = "HDRP/Lit";
        const string StandardShaderName = "Standard";
        const string DiffuseShaderName = "Diffuse";
        const string UnlitColorShaderName = "Unlit/Color";
        const string SpritesDefaultShaderName = "Sprites/Default";

#if UNITY_6000_7_OR_NEWER
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
#if UNITY_6000_7_OR_NEWER
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        static readonly int s_Color = Shader.PropertyToID("_Color");
#if UNITY_6000_7_OR_NEWER
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        static readonly int s_Metallic = Shader.PropertyToID("_Metallic");
#if UNITY_6000_7_OR_NEWER
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        static readonly int s_Smoothness = Shader.PropertyToID("_Smoothness");
#if UNITY_6000_7_OR_NEWER
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        static readonly int s_Glossiness = Shader.PropertyToID("_Glossiness");

        static Shader s_SampleLitShader = null;
        static Shader s_FallbackLitShader = null;

        public static Shader SampleLitShader => ResolveSampleLitShader();

        public static Shader ResolveSampleLitShader()
        {
            if (IsUsable(s_SampleLitShader))
                return s_SampleLitShader;

            s_SampleLitShader = Shader.Find(SampleLitShaderName);
            if (IsUsable(s_SampleLitShader))
                return s_SampleLitShader;

            return ResolveFallbackLitShader();
        }

        public static Material CreateSampleLitMaterial(string name, Color color, float metallic = 0f, float smoothness = 0.5f)
        {
            Material material = new Material(ResolveSampleLitShader()) { name = name };
            ConfigureSampleLitMaterial(material, color, metallic, smoothness);
            return material;
        }

        public static void ConfigureSampleLitMaterial(Material material, Color color, float metallic = 0f, float smoothness = 0.5f)
        {
            if (material == null)
                return;

            Shader shader = ResolveSampleLitShader();
            if (shader != null && material.shader != shader)
                material.shader = shader;

            SetColorIfPresent(material, s_BaseColor, color);
            SetColorIfPresent(material, s_Color, color);
            SetFloatIfPresent(material, s_Metallic, metallic);
            SetFloatIfPresent(material, s_Smoothness, smoothness);
            SetFloatIfPresent(material, s_Glossiness, smoothness);
        }

        public static bool IsUsable(Shader shader)
        {
            return shader != null && shader.isSupported;
        }

        static Shader ResolveFallbackLitShader()
        {
            if (IsUsable(s_FallbackLitShader))
                return s_FallbackLitShader;

            s_FallbackLitShader = FirstSupported(
                UniversalLitShaderName,
                HdrpLitShaderName,
                StandardShaderName,
                DiffuseShaderName,
                UnlitColorShaderName,
                SpritesDefaultShaderName);

            return s_FallbackLitShader;
        }

        static Shader FirstSupported(params string[] shaderNames)
        {
            for (int i = 0; i < shaderNames.Length; i++)
            {
                Shader shader = Shader.Find(shaderNames[i]);
                if (IsUsable(shader))
                    return shader;
            }

            return null;
        }

        static void SetColorIfPresent(Material material, int propertyId, Color value)
        {
            if (material.HasProperty(propertyId))
                material.SetColor(propertyId, value);
        }

        static void SetFloatIfPresent(Material material, int propertyId, float value)
        {
            if (material.HasProperty(propertyId))
                material.SetFloat(propertyId, value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            s_SampleLitShader = null;
            s_FallbackLitShader = null;
        }
    }
}
