using UnityEngine;

namespace ImpossibleRobert.Common.Timing
{
    public sealed class SimulationClock
    {
        double _time;
        int _frame;
        float _timeScale = 1f;

        public double Time => _time;
        public int Frame => _frame;
        public float TimeScale { get => _timeScale; set => _timeScale = Mathf.Max(0f, value); }

        public TimeStep Tick(float deltaTime)
        {
            float scaledDelta = Mathf.Max(0f, deltaTime) * _timeScale;
            _time += scaledDelta;
            _frame++;
            return CurrentStep(scaledDelta);
        }

        public TimeStep CurrentStep(float deltaTime = 0f)
        {
            return new TimeStep(Mathf.Max(0f, deltaTime), _time, _frame);
        }

        public void SetTime(double time)
        {
            _time = time;
        }

        public void Reset()
        {
            _time = 0d;
            _frame = 0;
            _timeScale = 1f;
        }
    }
}
