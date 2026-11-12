public partial class PlayerController
{
    private DungeonEncounterDirector _encounterDirector;

    /// <summary>Room combat is owned by the current dungeon.</summary>
    public bool IsInRoomCombat => _encounterDirector != null && _encounterDirector.IsPlayerInCombat(this);

    internal void BindEncounterDirector(DungeonEncounterDirector director) => _encounterDirector = director;

    internal void UnbindEncounterDirector(DungeonEncounterDirector director)
    {
        if (_encounterDirector == director) _encounterDirector = null;
    }
}
