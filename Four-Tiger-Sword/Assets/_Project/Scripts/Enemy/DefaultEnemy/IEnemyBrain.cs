/// <summary>Per-enemy AI runtime. Only its owning Enemy drives this lifecycle.</summary>
public interface IEnemyBrain
{
    string StateName { get; }
    void Initialize();
    void Tick(bool canAct);
    void SetPassive(bool passive);
    void Suspend();
    void Resume();
    bool TryStartSelfDestruct();
}
