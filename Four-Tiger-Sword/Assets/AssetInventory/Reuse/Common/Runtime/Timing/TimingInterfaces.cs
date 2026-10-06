namespace ImpossibleRobert.Common.Timing
{
    public interface ITimeStepReceiver
    {
        void Tick(in TimeStep step);
    }

    public interface ITimeAnimated
    {
        bool RequiresTimeUpdates { get; }
    }
}
