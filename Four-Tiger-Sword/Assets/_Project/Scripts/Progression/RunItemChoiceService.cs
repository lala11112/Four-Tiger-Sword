using System;
using System.Collections.Generic;
using System.Linq;

public enum RunItemChoiceKind { Starter, Room }

public sealed class RunItemChoiceSession
{
    public string Id { get; internal set; }
    public RunItemChoiceKind Kind { get; internal set; }
    public bool NeedsSynergy { get; internal set; }
    public string HeldId { get; internal set; }
    public int RerollIndex { get; internal set; }
    public bool Resolved { get; internal set; }
    internal readonly List<ItemDefinition> candidates = new();
    public IReadOnlyList<ItemDefinition> Candidates => candidates.AsReadOnly();
}

/// <summary>Deterministic, run-owned reward receipts. UI never grants items directly.</summary>
public sealed class RunItemChoiceService
{
    private readonly PlayerItemRuntime _items;
    private readonly PlayerWallet _wallet;
    private readonly EngravingSnapshot _snapshot;
    private readonly int _seed;
    private readonly Dictionary<string, RunItemChoiceSession> _receipts = new(StringComparer.Ordinal);
    private readonly Queue<RunItemChoiceSession> _pending = new();
    private bool _busy;
    public int RerollsRemaining { get; private set; }
    public int EmptyRewardGold => _snapshot.EmptyRewardGold;
    public string LastError { get; private set; }
    public event Action Changed;
    public RunItemChoiceSession Current
    {
        get { while (_pending.Count > 0 && _pending.Peek().Resolved) _pending.Dequeue(); return _pending.Count > 0 ? _pending.Peek() : null; }
    }
    public ItemCatalog Catalog => _items != null ? _items.Catalog : null;
    public RunItemChoiceService(PlayerItemRuntime items, PlayerWallet wallet, EngravingSnapshot snapshot, int seed)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        _snapshot = snapshot ?? EngravingSnapshot.Empty; _seed = seed;
        RerollsRemaining = _snapshot.Limit("reroll");
    }
    private bool Ready => !_busy && _items != null && _items.IsInitialized && !_items.IsMutationInProgress
        && _items.Catalog != null && _wallet != null && _wallet.IsInitialized
        && _items.GetComponent<PlayerStatManager>() is { CurrentHp: > 0f };
    public bool BeginStarterChoice()
    {
        if (!_snapshot.Has("heirloom")) return false;
        return Begin("starter", RunItemChoiceKind.Starter);
    }
    public bool BeginRoomReward(string receipt)
        => !string.IsNullOrWhiteSpace(receipt) && Begin("room:" + receipt, RunItemChoiceKind.Room);
    private bool Begin(string id, RunItemChoiceKind kind)
    {
        if (!Ready || _receipts.ContainsKey(id)) return false;
        var session = new RunItemChoiceSession { Id = id, Kind = kind, NeedsSynergy = kind == RunItemChoiceKind.Starter && _snapshot.Has("starterelement") };
        _receipts.Add(id, session); _pending.Enqueue(session);
        if (session.NeedsSynergy && StarterSynergies().Count == 0) session.NeedsSynergy = false;
        if (!session.NeedsSynergy)
            session.candidates.AddRange(Draw(Pool(kind == RunItemChoiceKind.Starter ? 1 : 0), Count(session), RandomFor(session), kind == RunItemChoiceKind.Room));
        LastError = null; Notify();
        return true;
    }
    public IReadOnlyList<ItemSynergyDefinition> StarterSynergies()
    {
        var pool = Pool(1);
        var result = new List<ItemSynergyDefinition>();
        foreach (var synergy in Catalog?.synergies ?? Array.Empty<ItemSynergyDefinition>())
            if (synergy?.members != null && pool.Exists(item => Array.IndexOf(synergy.members, item.itemId) >= 0)) result.Add(synergy);
        result.Sort((a, b) => StringComparer.Ordinal.Compare(a.id, b.id));
        return result;
    }
    public bool TryChooseStarterSynergy(string sessionId, string synergyId)
    {
        var current = Current;
        if (!Ready || current == null || current.Id != sessionId || !current.NeedsSynergy) return false;
        ItemSynergyDefinition selected = null;
        foreach (var synergy in StarterSynergies()) if (synergy.id == synergyId) selected = synergy;
        if (selected == null) return Fail("선택할 수 없는 계통입니다.");
        current.candidates.AddRange(Draw(Pool(1).FindAll(i => Array.IndexOf(selected.members, i.itemId) >= 0), Count(current), RandomFor(current), false));
        current.NeedsSynergy = false; LastError = null;
        if (current.candidates.Count == 1) return TryClaim(sessionId, current.candidates[0].itemId);
        Notify(); return true;
    }
    public bool TryClaim(string sessionId, string itemId)
    {
        var current = Current;
        if (!Ready || current == null || current.Id != sessionId || current.NeedsSynergy || current.Resolved) return false;
        var candidate = current.candidates.Find(i => i != null && i.itemId == itemId);
        if (candidate == null || Catalog.Find(itemId) != candidate) return Fail("유효하지 않은 보상입니다.");
        _busy = true;
        try
        {
            if (!_items.TryAddItem(itemId)) return Fail("아이템을 받을 수 없습니다. 보유 상태를 확인하세요.");
            current.Resolved = true; current.HeldId = null; LastError = null;
        }
        finally { _busy = false; }
        Notify(); return true;
    }
    public bool TryClaimEmptyReward(string sessionId)
    {
        var current = Current;
        if (!Ready || current == null || current.Id != sessionId || current.NeedsSynergy || current.candidates.Count != 0) return false;
        _busy = true;
        try
        {
            if (current.Kind == RunItemChoiceKind.Room && !_wallet.AddGold(_snapshot.EmptyRewardGold)) return Fail("대체 골드를 받을 수 없습니다.");
            current.Resolved = true; LastError = null;
        }
        finally { _busy = false; }
        Notify(); return true;
    }
    public bool TrySkip(string sessionId)
    {
        var current = Current;
        if (!Ready || current == null || current.Id != sessionId) return false;
        current.Resolved = true; current.HeldId = null; LastError = null; Notify(); return true;
    }
    public bool CanHold => _snapshot.Has("hold") && Current?.Kind == RunItemChoiceKind.Room;
    public bool ToggleHeldCandidate(string sessionId, string itemId)
    {
        var current = Current;
        if (!Ready || !CanHold || current.Id != sessionId || !current.candidates.Exists(i => i.itemId == itemId)) return false;
        current.HeldId = current.HeldId == itemId ? null : itemId; Notify(); return true;
    }
    public bool CanReroll
    {
        get
        {
            var current = Current;
            if (!Ready || current == null || current.Kind != RunItemChoiceKind.Room || current.NeedsSynergy || RerollsRemaining <= 0) return false;
            var currentIds = new HashSet<string>(current.candidates.ConvertAll(i => i.itemId), StringComparer.Ordinal);
            return current.candidates.Count > (current.HeldId != null ? 1 : 0) && Pool(0).Exists(i => !currentIds.Contains(i.itemId));
        }
    }
    public bool TryReroll(string sessionId)
    {
        var current = Current;
        if (!CanReroll || current.Id != sessionId) return false;
        var old = new List<ItemDefinition>(current.candidates);
        var held = old.Find(i => i.itemId == current.HeldId);
        var ids = new HashSet<string>(old.ConvertAll(i => i.itemId), StringComparer.Ordinal);
        var fresh = Pool(0).FindAll(i => !ids.Contains(i.itemId));
        current.RerollIndex++;
        var random = RandomFor(current);
        var next = new List<ItemDefinition>();
        if (held != null) next.Add(held);
        next.AddRange(Draw(fresh, Count(current) - next.Count, random, true));
        if (next.Count < Count(current)) next.AddRange(Draw(old.FindAll(i => i != held && !Owned(i)), Count(current) - next.Count, random, true));
        current.candidates.Clear(); current.candidates.AddRange(next);
        RerollsRemaining--; LastError = null; Notify(); return true;
    }
    public float RewardWeight(ItemDefinition item)
    {
        if (!_snapshot.Has("synergy") || item == null) return 1f;
        foreach (var state in _items.SynergyStates)
        {
            if (state.Definition?.members == null || Array.IndexOf(state.Definition.members, item.itemId) < 0) continue;
            int next = int.MaxValue;
            foreach (var tier in state.Definition.tiers ?? Array.Empty<ItemSynergyTier>())
                if (tier != null && tier.count > state.OwnedCount) next = Math.Min(next, tier.count);
            if (next == state.OwnedCount + 1) return Math.Max(1f, _snapshot.Amount("synergy"));
        }
        return 1f;
    }
    private int Count(RunItemChoiceSession session) => session.Kind == RunItemChoiceKind.Starter
        ? Math.Max(1, _snapshot.Limit("heirloom")) : Math.Max(1, _snapshot.BaseRewardChoices + (int)_snapshot.Amount("choices"));
    private bool Owned(ItemDefinition item)
    {
        foreach (var owned in _items.OwnedItems)
            if (owned != null && (owned.itemId == item.itemId || owned.candidateKey == item.candidateKey)) return true;
        return false;
    }
    private List<ItemDefinition> Pool(int grade)
    {
        var result = new List<ItemDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Catalog?.items ?? Array.Empty<ItemDefinition>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || string.IsNullOrWhiteSpace(item.candidateKey)
                || item.projectId != Catalog.projectId || Catalog.Find(item.itemId) != item || item.grade < 1 || item.grade > 4
                || grade != 0 && item.grade != grade || Owned(item) || item.effects == null || item.boundaryEffects == null) continue;
            if (item.effects.Any(e => e == null || !e.IsValid()) || item.boundaryEffects.Any(e => e == null || !e.IsValid())) continue;
            if (ids.Add(item.itemId) && keys.Add(item.candidateKey)) result.Add(item);
        }
        result.Sort((a, b) => StringComparer.Ordinal.Compare(a.itemId, b.itemId)); return result;
    }
    private List<ItemDefinition> Draw(List<ItemDefinition> pool, int count, Random random, bool weighted)
    {
        var result = new List<ItemDefinition>();
        while (result.Count < count && pool.Count > 0)
        {
            double total = 0; foreach (var item in pool) total += weighted ? RewardWeight(item) : 1;
            double roll = random.NextDouble() * total; int index = pool.Count - 1;
            for (int i = 0; i < pool.Count; i++) { roll -= weighted ? RewardWeight(pool[i]) : 1; if (roll < 0) { index = i; break; } }
            result.Add(pool[index]); pool.RemoveAt(index);
        }
        return result;
    }
    private Random RandomFor(RunItemChoiceSession session)
    {
        unchecked { int hash = _seed ^ 0x52455744; foreach (char c in session.Id) hash = (hash ^ c) * 16777619; return new Random(hash ^ (session.RerollIndex * 486187739)); }
    }
    private bool Fail(string message) { LastError = message; return false; }
    private void Notify()
    {
        if (Changed == null) return;
        foreach (Action handler in Changed.GetInvocationList()) try { handler(); } catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
    }
}

/// <summary>A regenerated stage can only earn rewards above its previous maximum clear count.</summary>
public sealed class RunItemRewardLedger
{
    private readonly Dictionary<(int chapter, int stage), int> _highest = new();
    private readonly HashSet<int> _rooms = new();
    private (int chapter, int stage) _stage;
    public void BeginGeneration(int chapter, int stage) { _stage = (chapter, stage); _rooms.Clear(); }
    public bool TryRecord(int roomId, out string receipt)
    {
        receipt = null;
        if (!_rooms.Add(roomId)) return false;
        _highest.TryGetValue(_stage, out int high);
        if (_rooms.Count <= high) return false;
        _highest[_stage] = _rooms.Count;
        receipt = _stage.chapter + ":" + _stage.stage + ":" + _rooms.Count;
        return true;
    }
}
