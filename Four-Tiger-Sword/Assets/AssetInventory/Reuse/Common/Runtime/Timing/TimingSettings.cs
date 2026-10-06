using System;
using UnityEngine;

namespace ImpossibleRobert.Common.Timing
{
    [Serializable]
    public struct FrameTimingSettings
    {
        [SerializeField] ClockMode _clockMode;
        [SerializeField] UpdatePhase _updatePhase;
        [SerializeField, Min(0f)] float _maxDeltaTime;
        [SerializeField] bool _tickInEditMode;
        [SerializeField, Min(0f)] float _timeScale;

        public ClockMode ClockMode { get => _clockMode; set => _clockMode = value; }
        public UpdatePhase UpdatePhase { get => _updatePhase; set => _updatePhase = value; }
        public float MaxDeltaTime { get => _maxDeltaTime; set => _maxDeltaTime = Mathf.Max(0f, value); }
        public bool TickInEditMode { get => _tickInEditMode; set => _tickInEditMode = value; }
        public float TimeScale { get => _timeScale <= 0f ? 1f : _timeScale; set => _timeScale = Mathf.Max(0f, value); }

        public static FrameTimingSettings Default(float maxDeltaTime = 0.1f, bool tickInEditMode = true)
        {
            return new FrameTimingSettings
            {
                _clockMode = ClockMode.Scaled,
                _updatePhase = UpdatePhase.Update,
                _maxDeltaTime = Mathf.Max(0f, maxDeltaTime),
                _tickInEditMode = tickInEditMode,
                _timeScale = 1f
            };
        }
    }

    [Serializable]
    public struct UpdateScheduleSettings
    {
        [SerializeField] UpdateCadence _cadence;
        [SerializeField, Min(1f)] float _fixedRateHz;

        public UpdateCadence Cadence { get => _cadence; set => _cadence = value; }
        public float FixedRateHz { get => _fixedRateHz <= 0f ? 30f : _fixedRateHz; set => _fixedRateHz = Mathf.Max(1f, value); }

        public static UpdateScheduleSettings EveryFrame => Create(UpdateCadence.EveryFrame);
        public static UpdateScheduleSettings OnChange => Create(UpdateCadence.OnChange);
        public static UpdateScheduleSettings FixedRate(float fixedRateHz) => Create(UpdateCadence.FixedRate, fixedRateHz);
        public static UpdateScheduleSettings Manual => Create(UpdateCadence.Manual);

        public static UpdateScheduleSettings Create(UpdateCadence cadence, float fixedRateHz = 30f)
        {
            return new UpdateScheduleSettings
            {
                _cadence = cadence,
                _fixedRateHz = Mathf.Max(1f, fixedRateHz)
            };
        }
    }
}
