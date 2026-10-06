namespace ImpossibleRobert.Common.Timing
{
    public readonly struct TimeStep
    {
        public TimeStep(float deltaTime, double time, int frame)
        {
            DeltaTime = deltaTime;
            Time = time;
            Frame = frame;
        }

        public float DeltaTime { get; }
        public double Time { get; }
        public int Frame { get; }

        public static TimeStep Zero => new TimeStep(0f, 0d, 0);
    }
}
