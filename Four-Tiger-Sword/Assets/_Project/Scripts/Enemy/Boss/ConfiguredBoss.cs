using System.Collections.Generic;

/// <summary>Attach this component and configure PhaseDatas for a boss without a custom actor class.</summary>
public sealed class ConfiguredBoss : Boss
{
    protected override List<IBossPhase> CreatePhases()
    {
        var phases = new List<IBossPhase>();
        if (PhaseDatas != null)
            foreach (var data in PhaseDatas)
                if (data != null) phases.Add(new DataPhase(data));
        return phases;
    }

    private sealed class DataPhase : BossPhaseBase
    {
        public DataPhase(BossPhaseData data) : base(data) { }
    }
}