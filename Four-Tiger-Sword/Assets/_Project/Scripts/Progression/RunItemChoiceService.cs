using System;
using System.Collections.Generic;
using System.Linq;

public enum RunItemChoiceKind { Room }

public sealed class RunItemChoiceSession
{
    public string Id { get; internal set; }
    public RunItemChoiceKind Kind { get; internal set; }
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
    public string LastError { get; private set; }
    public event Action Changed;
    public event Action OwnedItemsChanged
    {
        add { if (_items != null) _items.OnOwnedItemsChanged += value; }
        remove { if (_items != null) _items.OnOwnedItemsChanged -= value; }
    }
    public RunItemChoiceSession Current
    {
        get { while (_pending.Count > 0 && _pending.Peek().Resolved) _pending.Dequeue(); return _pending.Count > 0 ? _pending.Peek() : null; }
    }
    public ItemCatalog Catalog => _items != null ? _items.Catalog : null;
    public IReadOnlyList<ItemSynergyState> OwnedSynergies => _items.SynergyStates;
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
    public bool BeginRoomReward(string receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt) || !Ready) return false;
        string id = "room:" + receipt;
        if (_receipts.ContainsKey(id)) return false;
        var session = new RunItemChoiceSession { Id = id, Kind = RunItemChoiceKind.Room };
        session.candidates.AddRange(Draw(Pool(), CandidateCount, RandomFor(session)));
        if (session.candidates.Count == 0)
        {
            // A failed credit leaves the receipt available at the chest for a later attempt.
            _busy = true;
            try
            {
                if (!_wallet.TryAddEarnedGold(_snapshot.EmptyRewardGold)) return Fail("대체 골드를 받을 수 없습니다.");
                session.Resolved = true;
                _receipts.Add(id, session);
            }
            finally { _busy = false; }
        }
        else
        {
            _receipts.Add(id, session);
            _pending.Enqueue(session);
        }
        LastError = null; Notify();
        return true;
    }
    public bool TryClaim(string sessionId, string itemId)
    {
        var current = Current;
        if (!Ready || current == null || current.Id != sessionId || current.Resolved) return false;
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
    public bool TrySkip(string sessionId)
    {
        var current = Current;
        if (!Ready || current == null || current.Id != sessionId) return false;
        current.Resolved = true; current.HeldId = null; LastError = null; Notify(); return true;
    }
    public bool CanHold => _snapshot.Has("hold") && Current != null;
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
            if (!Ready || current == null || RerollsRemaining <= 0) return false;
            var currentIds = new HashSet<string>(current.candidates.ConvertAll(i => i.itemId), StringComparer.Ordinal);
            return current.candidates.Count > (current.HeldId != null ? 1 : 0) && Pool().Exists(i => !currentIds.Contains(i.itemId));
        }
    }
    public bool TryReroll(string sessionId)
    {
        var current = Current;
        if (!CanReroll || current.Id != sessionId) return false;
        var old = new List<ItemDefinition>(current.candidates);
        var held = old.Find(i => i.itemId == current.HeldId);
        var ids = new HashSet<string>(old.ConvertAll(i => i.itemId), StringComparer.Ordinal);
        var fresh = Pool().FindAll(i => !ids.Contains(i.itemId));
        current.RerollIndex++;
        var random = RandomFor(current);
        var next = new List<ItemDefinition>();
        if (held != null) next.Add(held);
        next.AddRange(Draw(fresh, CandidateCount - next.Count, random));
        if (next.Count < CandidateCount) next.AddRange(Draw(old.FindAll(i => i != held && !Owned(i)), CandidateCount - next.Count, random));
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
    private int CandidateCount => Math.Max(1, _snapshot.BaseRewardChoices + (int)_snapshot.Amount("choices"));
    private bool Owned(ItemDefinition item)
    {
        foreach (var owned in _items.OwnedItems)
            if (owned != null && (owned.itemId == item.itemId || owned.candidateKey == item.candidateKey)) return true;
        return false;
    }
    private List<ItemDefinition> Pool()
    {
        var result = new List<ItemDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Catalog?.items ?? Array.Empty<ItemDefinition>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || string.IsNullOrWhiteSpace(item.candidateKey)
                || item.projectId != Catalog.projectId || Catalog.Find(item.itemId) != item || item.grade < 1 || item.grade > 4
                || Owned(item) || item.effects == null || item.boundaryEffects == null) continue;
            if (item.effects.Any(e => e == null || !e.IsValid()) || item.boundaryEffects.Any(e => e == null || !e.IsValid())) continue;
            if (ids.Add(item.itemId) && keys.Add(item.candidateKey)) result.Add(item);
        }
        result.Sort((a, b) => StringComparer.Ordinal.Compare(a.itemId, b.itemId)); return result;
    }
    private List<ItemDefinition> Draw(List<ItemDefinition> pool, int count, Random random)
    {
        var result = new List<ItemDefinition>();
        while (result.Count < count && pool.Count > 0)
        {
            double total = 0; foreach (var item in pool) total += RewardWeight(item);
            double roll = random.NextDouble() * total; int index = pool.Count - 1;
            for (int i = 0; i < pool.Count; i++) { roll -= RewardWeight(pool[i]); if (roll < 0) { index = i; break; } }
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
