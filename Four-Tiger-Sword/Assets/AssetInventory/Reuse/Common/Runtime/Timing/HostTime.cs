using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace ImpossibleRobert.Common.Timing
{
    public static class HostTime
    {
        public const float DefaultMaxDeltaTime = 0.1f;

        public static float DeltaTime(ClockMode clockMode = ClockMode.Scaled, float maxDeltaTime = DefaultMaxDeltaTime)
        {
            if (!Application.isPlaying)
                return 0f;

            float deltaTime = clockMode switch
            {
                ClockMode.Unscaled => Time.unscaledDeltaTime,
                ClockMode.Realtime => Time.unscaledDeltaTime,
                ClockMode.Manual => 0f,
                _ => Time.deltaTime
            };
            return ClampDelta(deltaTime, maxDeltaTime);
        }

        public static float GetDeltaTime(ref double lastHostTime, float maxDeltaTime = DefaultMaxDeltaTime, ClockMode clockMode = ClockMode.Scaled)
        {
            if (clockMode == ClockMode.Manual)
                return 0f;

            if (Application.isPlaying && clockMode != ClockMode.Realtime)
                return DeltaTime(clockMode, maxDeltaTime);

            double now = CurrentHostTime(clockMode);
            if (lastHostTime <= 0d)
            {
                lastHostTime = now;
                return 0f;
            }

            float deltaTime = Mathf.Max(0f, (float)(now - lastHostTime));
            lastHostTime = now;
            return ClampDelta(deltaTime, maxDeltaTime);
        }

        public static float GetHostDeltaTime(ref double lastHostTime, in FrameTimingSettings settings)
        {
            return GetDeltaTime(ref lastHostTime, settings.MaxDeltaTime, settings.ClockMode);
        }

        public static float GetScaledDeltaTime(ref double lastHostTime, in FrameTimingSettings settings)
        {
            return GetHostDeltaTime(ref lastHostTime, settings) * settings.TimeScale;
        }

        public static void Reset(ref double lastHostTime, ClockMode clockMode = ClockMode.Scaled)
        {
            lastHostTime = Application.isPlaying && clockMode != ClockMode.Realtime
                ? 0d
                : CurrentHostTime(clockMode);
        }

        public static void Reset(ref double lastHostTime, in FrameTimingSettings settings)
        {
            Reset(ref lastHostTime, settings.ClockMode);
        }

        public static double CurrentHostTime(ClockMode clockMode = ClockMode.Scaled)
        {
            if (Application.isPlaying)
            {
                return clockMode switch
                {
                    ClockMode.Unscaled => Time.unscaledTimeAsDouble,
                    ClockMode.Realtime => Time.realtimeSinceStartupAsDouble,
                    ClockMode.Manual => 0d,
                    _ => Time.timeAsDouble
                };
            }

#if UNITY_EDITOR
            return EditorApplication.timeSinceStartup;
#else
            return 0d;
#endif
        }

        public static TimeStep Step(ref double lastHostTime, int frame, float maxDeltaTime = DefaultMaxDeltaTime, ClockMode clockMode = ClockMode.Scaled)
        {
            float deltaTime = GetDeltaTime(ref lastHostTime, maxDeltaTime, clockMode);
            return new TimeStep(deltaTime, CurrentHostTime(clockMode), frame);
        }

        public static TimeStep Step(ref double lastHostTime, int frame, in FrameTimingSettings settings)
        {
            float deltaTime = GetScaledDeltaTime(ref lastHostTime, settings);
            return new TimeStep(deltaTime, CurrentHostTime(settings.ClockMode), frame);
        }

        public static float ClampDelta(float deltaTime, float maxDeltaTime)
        {
            if (maxDeltaTime <= 0f)
                return Mathf.Max(0f, deltaTime);

            return Mathf.Clamp(deltaTime, 0f, maxDeltaTime);
        }

        public static void RequestEditorFrame(Object target = null, bool repaintSceneViews = true, bool repaintAllViews = true)
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;

            EditorApplication.QueuePlayerLoopUpdate();
            if (repaintSceneViews)
                SceneView.RepaintAll();
            if (repaintAllViews)
                InternalEditorUtility.RepaintAllViews();
#endif
        }
    }
}
