using UnityEngine;

namespace ImpossibleRobert.Common.Timing
{
    public sealed class UpdateSchedule
    {
        UpdateScheduleSettings _settings;
        float _fixedAccumulator;
        bool _manualDirty = true;
        bool _hasUpdated;

        public UpdateSchedule(UpdateScheduleSettings settings)
        {
            _settings = settings;
        }

        public UpdateScheduleSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                _settings.FixedRateHz = _settings.FixedRateHz;
            }
        }

        public bool ManualDirty => _manualDirty;
        public bool HasUpdated => _hasUpdated;

        public void MarkManualDirty()
        {
            _manualDirty = true;
        }

        public void Reset()
        {
            _fixedAccumulator = 0f;
            _manualDirty = true;
            _hasUpdated = false;
        }

        public bool ShouldUpdate(float deltaTime, bool isDirty, bool requiresTimeUpdates)
        {
            if (!_hasUpdated)
            {
                _hasUpdated = true;
                _manualDirty = false;
                return true;
            }

            bool shouldUpdate;
            switch (_settings.Cadence)
            {
                case UpdateCadence.EveryFrame:
                    shouldUpdate = true;
                    break;
                case UpdateCadence.OnChange:
                    shouldUpdate = isDirty || requiresTimeUpdates;
                    break;
                case UpdateCadence.FixedRate:
                    shouldUpdate = ShouldFixedRateUpdate(deltaTime);
                    break;
                case UpdateCadence.Manual:
                    shouldUpdate = _manualDirty;
                    break;
                default:
                    shouldUpdate = isDirty;
                    break;
            }

            if (shouldUpdate)
                _manualDirty = false;

            return shouldUpdate;
        }

        bool ShouldFixedRateUpdate(float deltaTime)
        {
            _fixedAccumulator += Mathf.Max(0f, deltaTime);
            float interval = 1f / _settings.FixedRateHz;
            if (_fixedAccumulator < interval)
                return false;

            _fixedAccumulator = Mathf.Repeat(_fixedAccumulator, interval);
            return true;
        }
    }
}
