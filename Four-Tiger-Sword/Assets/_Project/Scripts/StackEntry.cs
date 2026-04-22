public class StackEntry
{
    public StackDataSO Data;
    public int CurrentStack;
    public float Timer;

    public StackEntry(StackDataSO data)
    {
        Data = data;
        CurrentStack = 0;
        Timer = 0f;
    }

    public bool IsEmpty => CurrentStack == 0;
    public bool IsMaxed => CurrentStack >= Data.maxStack;
}