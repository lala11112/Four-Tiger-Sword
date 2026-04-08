using System; 

public class Transition
{
    public IPlayerState To{ get; }
    public Func<bool> Condition{ get; }

    public Transition(IPlayerState to, Func<bool> condition)
    {
        To = to;
        Condition = condition;
    }
}
