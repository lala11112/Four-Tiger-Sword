namespace ImpossibleRobert.Common.Timing
{
    public enum ClockMode
    {
        Scaled = 0,
        Unscaled = 1,
        Realtime = 2,
        Manual = 3
    }

    public enum UpdatePhase
    {
        Update = 0,
        LateUpdate = 1,
        FixedUpdate = 2,
        Manual = 3
    }

    public enum UpdateCadence
    {
        EveryFrame = 0,
        OnChange = 1,
        FixedRate = 2,
        Manual = 3
    }
}
