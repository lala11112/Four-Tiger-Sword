using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class SaveSlotInfo
{
    public int Number { get; }
    public string Id { get; }
    public string Name { get; }
    public bool IsReadable { get; }
    public string Error { get; }
    public int Balance { get; }
    public TutorialProgressStatus TutorialStatus { get; }

    internal SaveSlotInfo(int number, string id, string name, bool isReadable, string error,
        int balance = 0, TutorialProgressStatus tutorialStatus = TutorialProgressStatus.NotStarted)
    {
        Number = number;
        Id = id;
        Name = name;
        IsReadable = isReadable;
        Error = error;
        Balance = balance;
        TutorialStatus = tutorialStatus;
    }
}

/// <summary>Owns storage and the one selected progression account for the current game session.</summary>
public sealed class SaveSlotService
{
    public const int MaxSlots = 5;
    public const int MaxNameLength = 20;
    private const string ProfileFileName = "player-upgrades.json";
    private const string MetadataFileName = "slot.json";
    private const string LegacyMarkerName = "legacy-imported";
    private const string LastPlayedFileName = "last-played-slot";
    private const string LegacySlotId = "b20d0ad769b043f7a6d256cb56e902e0";

    private static SaveSlotService _instance;
    public static SaveSlotService Instance => _instance ??= new SaveSlotService(Application.persistentDataPath,
        Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings"));

    private readonly string _persistentRoot;
    private readonly string _slotRoot;
    private readonly PlayerUpgradeSettingsSO _settings;
    private readonly EngravingSettingsSO _engravings;
    private int _selectedSlotNumber;

    public IReadOnlyList<SaveSlotInfo> Slots { get; private set; } = Array.Empty<SaveSlotInfo>();
    public string SelectedSlotId { get; private set; }
    public PlayerUpgradeService SelectedAccount { get; private set; }

    public SaveSlotService(string persistentRoot, PlayerUpgradeSettingsSO settings, EngravingSettingsSO engravings = null)
    {
        if (string.IsNullOrWhiteSpace(persistentRoot)) throw new ArgumentException("A save root is required.", nameof(persistentRoot));
        _persistentRoot = Path.GetFullPath(persistentRoot);
        _slotRoot = Path.Combine(_persistentRoot, "save-slots");
        _settings = settings;
        _engravings = engravings;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => _instance = null;

#if UNITY_EDITOR
    public static IDisposable OverrideForTesting(SaveSlotService service)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));
        var scope = new TestOverride(_instance, service);
        _instance = service;
        return scope;
    }

    private sealed class TestOverride : IDisposable
    {
        private readonly SaveSlotService _previous, _replacement;
        private bool _disposed;

        public TestOverride(SaveSlotService previous, SaveSlotService replacement)
        { _previous = previous; _replacement = replacement; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(_instance, _replacement)) _instance = _previous;
        }
    }
#endif

    public bool TryRefresh(out string error)
    {
        error = null;
        try
        {
            EnsureLegacyImported();
            var slots = new List<SaveSlotInfo>();
            for (int number = 1; number <= MaxSlots; number++)
            {
                var slot = ReadSlot(number);
                if (slot != null) slots.Add(slot);
            }
            Slots = slots.AsReadOnly();
            return true;
        }
        catch (Exception failure)
        {
            error = "저장 슬롯 목록을 불러오지 못했습니다. " + failure.Message;
            return false;
        }
    }

    public bool TryCreate(int number, string name, out SaveSlotInfo slot, out string error)
    {
        slot = null;
        error = null;
        if (number < 1 || number > MaxSlots) { error = "올바른 저장 슬롯을 선택하세요."; return false; }
        if (!TryNormalizeName(number, name, out string normalized))
        { error = $"슬롯 이름은 줄바꿈 없이 {MaxNameLength}자까지 입력할 수 있습니다."; return false; }
        if (!TryRefresh(out error)) return false;
        foreach (var existing in Slots)
            if (existing.Number == number) { error = "이미 저장된 슬롯입니다."; return false; }

        string id = Guid.NewGuid().ToString("N");
        try
        {
            PublishSlot(number, id, normalized, directory =>
            {
                var store = new PlayerUpgradeFileStore(Path.Combine(directory, ProfileFileName), _engravings);
                if (!store.TrySave(PlayerUpgradeProfile.New(), out string saveError)) throw new IOException(saveError);
                var probe = new PlayerUpgradeService(_settings, store, _engravings);
                if (!probe.IsReady) throw new IOException(probe.LastError);
            });
            slot = new SaveSlotInfo(number, id, normalized, true, null);
            var current = new List<SaveSlotInfo>(Slots) { slot };
            current.Sort((a, b) => a.Number.CompareTo(b.Number));
            Slots = current.AsReadOnly();
            return true;
        }
        catch (Exception failure)
        {
            error = "새 저장 슬롯을 만들지 못했습니다. " + failure.Message;
            return false;
        }
    }

    public bool TryGetLastPlayedSlot(out SaveSlotInfo slot, out string error)
    {
        slot = null;
        error = null;
        string id;
        try
        {
            string path = Path.Combine(_slotRoot, LastPlayedFileName);
            if (!TryGetAttributes(path, out var attributes)) return false;
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("마지막 저장 슬롯 기록이 올바른 파일이 아닙니다.");
            id = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (!Guid.TryParseExact(id, "N", out _))
                throw new InvalidDataException("마지막 저장 슬롯 기록의 형식이 올바르지 않습니다.");
        }
        catch (Exception failure)
        {
            error = "이어할 저장 기록을 불러오지 못했습니다. " + failure.Message;
            return false;
        }

        if (!TryFindSlot(id, out var found, out error)) return false;
        if (!found.IsReadable) { error = found.Error; return false; }
        slot = found;
        return true;
    }

    public bool TrySelect(string id, out PlayerUpgradeService account, out string error)
    {
        account = null;
        if (!TryFindSlot(id, out var slot, out error)) return false;
        if (!slot.IsReadable) { error = slot.Error; return false; }
        var candidate = SelectedSlotId == slot.Id && SelectedAccount?.IsReady == true ? SelectedAccount
            : OpenAccount(SlotAccountDirectory(slot.Number, slot.Id));
        if (!candidate.IsReady) { error = candidate.LastError; return false; }
        if (!TryWriteLastPlayedSlot(slot.Id, out error)) return false;
        bool changed = SelectedSlotId != slot.Id;
        _selectedSlotNumber = slot.Number;
        SelectedSlotId = slot.Id;
        SelectedAccount = account = candidate;
        if (changed) TutorialSession.Reset();
        return true;
    }

    private bool TryWriteLastPlayedSlot(string id, out string error)
    {
        error = null;
        string path = Path.Combine(_slotRoot, LastPlayedFileName);
        string pending = Path.Combine(_slotRoot, ".last-played-" + Guid.NewGuid().ToString("N"));
        try
        {
            bool exists = TryGetAttributes(path, out var attributes);
            if (exists && (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("마지막 저장 슬롯 기록을 교체할 수 없습니다.");
            WriteFlushed(pending, id + "\n");
            if (exists) File.Replace(pending, path, null);
            else File.Move(pending, path);
            return true;
        }
        catch (Exception failure)
        {
            error = "마지막으로 선택한 저장 슬롯을 기록하지 못했습니다. " + failure.Message;
            return false;
        }
        finally { CleanupIgnoredPath(pending); }
    }

    public bool TryDelete(string id, out string error)
    {
        if (!TryFindSlot(id, out var slot, out error)) return false;
        string source = SlotDirectory(slot.Number);
        string tombstone = Path.Combine(_slotRoot, ".deleted-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!TryGetAttributes(source, out var attributes) || (attributes & FileAttributes.Directory) == 0)
                throw new IOException("저장 슬롯이 사라졌습니다.");
            Directory.Move(source, tombstone);
        }
        catch (Exception failure)
        {
            error = "저장 슬롯을 삭제하지 못했습니다. " + failure.Message;
            return false;
        }

        // Once renamed, the slot is gone from the list even if cleanup has to wait.
        var current = new List<SaveSlotInfo>(Slots);
        current.Remove(slot);
        Slots = current.AsReadOnly();
        if (_selectedSlotNumber == slot.Number)
        {
            _selectedSlotNumber = 0;
            SelectedSlotId = null;
            SelectedAccount = null;
            TutorialSession.Reset();
        }
        CleanupIgnoredPath(tombstone);
        return true;
    }

    private bool TryFindSlot(string id, out SaveSlotInfo slot, out string error)
    {
        slot = null;
        if (!TryRefresh(out error)) return false;
        foreach (var candidate in Slots)
            if (candidate.Id == id) { slot = candidate; return true; }
        error = "해당 저장 슬롯을 찾을 수 없습니다.";
        return false;
    }

    private SaveSlotInfo ReadSlot(int number)
    {
        string path = SlotDirectory(number);
        string id = $"unreadable-{number}";
        string name = DefaultName(number);
        try
        {
            if (!TryGetAttributes(path, out var attributes)) return null;
            if ((attributes & FileAttributes.Directory) == 0) return null;
            if (!IsOrdinaryDirectory(attributes)) throw new IOException("저장 슬롯 폴더를 확인할 수 없습니다.");
            string identityDirectory = null;
            foreach (string child in Directory.EnumerateDirectories(path))
            {
                string childName = Path.GetFileName(child);
                if (!Guid.TryParseExact(childName, "N", out _)) continue;
                if (identityDirectory != null) throw new IOException("저장 슬롯 식별자가 여러 개입니다.");
                identityDirectory = child;
            }
            if (identityDirectory == null || !TryGetAttributes(identityDirectory, out attributes) || !IsOrdinaryDirectory(attributes))
                throw new IOException("저장 슬롯 식별자가 없습니다.");
            id = Path.GetFileName(identityDirectory);
            var metadata = JsonUtility.FromJson<SlotMetadata>(File.ReadAllText(Path.Combine(identityDirectory, MetadataFileName), Encoding.UTF8));
            if (metadata == null || metadata.version != 1 || !TryNormalizeName(number, metadata.name, out name))
                throw new IOException("저장 슬롯 이름 정보를 읽을 수 없습니다.");
            var account = OpenAccount(identityDirectory);
            return new SaveSlotInfo(number, id, name, account.IsReady, account.LastError, account.Balance, account.TutorialStatus);
        }
        catch (Exception failure)
        {
            return new SaveSlotInfo(number, id, name ?? DefaultName(number), false,
                "저장 슬롯을 읽을 수 없습니다. " + failure.Message);
        }
    }

    private PlayerUpgradeService OpenAccount(string identityDirectory) => new PlayerUpgradeService(_settings,
        new SlotProfileStore(identityDirectory, _engravings), _engravings);

    private string SlotDirectory(int number) => Path.Combine(_slotRoot, $"slot-{number}");
    private string SlotAccountDirectory(int number, string id) => Path.Combine(SlotDirectory(number), id);
    private static string DefaultName(int number) => $"슬롯 {number}";

    private static bool TryNormalizeName(int number, string name, out string normalized)
    {
        normalized = (name ?? string.Empty).Trim();
        if (normalized.Length > MaxNameLength) return false;
        foreach (char character in normalized)
            if (char.IsControl(character)) return false;
        if (normalized.Length == 0) normalized = DefaultName(number);
        return true;
    }

    private void EnsureLegacyImported()
    {
        string marker = Path.Combine(_slotRoot, LegacyMarkerName);
        if (TryGetAttributes(marker, out var markerAttributes))
        {
            if ((markerAttributes & FileAttributes.Directory) != 0) throw new IOException("이전 저장 가져오기 기록이 잘못되었습니다.");
            return;
        }

        string original = Path.Combine(_persistentRoot, ProfileFileName);
        bool hasPrimary = TryGetAttributes(original, out _);
        bool hasBackup = TryGetAttributes(original + ".bak", out _);
        if (hasPrimary || hasBackup)
        {
            string publishedLegacy = SlotAccountDirectory(1, LegacySlotId);
            // If the app stopped after publishing slot 1, finish its marker without importing again.
            if (!Directory.Exists(publishedLegacy))
            {
                if (TryGetAttributes(SlotDirectory(1), out _))
                    throw new IOException("기존 저장을 가져올 슬롯 1이 이미 사용 중입니다.");
                PublishSlot(1, LegacySlotId, "기존 데이터", directory =>
                {
                    string migrated = Path.Combine(directory, ProfileFileName);
                    if (hasPrimary) CopyFlushed(original, migrated);
                    if (hasBackup) CopyFlushed(original + ".bak", migrated + ".bak");
                });
            }
        }

        Directory.CreateDirectory(_slotRoot);
        string pendingMarker = Path.Combine(_slotRoot, ".legacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteFlushed(pendingMarker, "1\n");
            File.Move(pendingMarker, marker);
        }
        finally { CleanupIgnoredPath(pendingMarker); }
    }

    private void PublishSlot(int number, string id, string name, Action<string> writeProfile)
    {
        Directory.CreateDirectory(_slotRoot);
        string staging = Path.Combine(_slotRoot, ".creating-" + Guid.NewGuid().ToString("N"));
        try
        {
            string directory = Path.Combine(staging, id);
            Directory.CreateDirectory(directory);
            WriteFlushed(Path.Combine(directory, MetadataFileName), JsonUtility.ToJson(new SlotMetadata { version = 1, name = name }));
            writeProfile(directory);
            Directory.Move(staging, SlotDirectory(number));
        }
        finally { CleanupIgnoredPath(staging); }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        attributes = default;
        try { attributes = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool IsOrdinaryDirectory(FileAttributes attributes) =>
        (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;

    private static void WriteFlushed(string path, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static void CopyFlushed(string source, string target)
    {
        using var input = File.OpenRead(source);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(true);
    }

    private static void CleanupIgnoredPath(string path)
    {
        try
        {
            if (!TryGetAttributes(path, out var attributes)) return;
            if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path, IsOrdinaryDirectory(attributes));
            else File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Serializable]
    private sealed class SlotMetadata
    {
        public int version;
        public string name;
    }

    // A published slot must never turn into a fresh profile if both copies disappear. Reject stale writers after deletion.
    private sealed class SlotProfileStore : IPlayerUpgradeStore
    {
        private readonly string _profilePath;
        private readonly PlayerUpgradeFileStore _files;

        public SlotProfileStore(string directory, EngravingSettingsSO engravings)
        {
            _profilePath = Path.Combine(directory, ProfileFileName);
            _files = new PlayerUpgradeFileStore(_profilePath, engravings);
        }

        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        {
            profile = null;
            return HasProfile(out error) && _files.TryLoad(out profile, out error);
        }

        public bool TrySave(PlayerUpgradeProfile profile, out string error) =>
            HasProfile(out error) && _files.TrySave(profile, out error);

        private bool HasProfile(out string error)
        {
            error = null;
            try
            {
                if (TryGetAttributes(_profilePath, out _) || TryGetAttributes(_profilePath + ".bak", out _)) return true;
                error = "저장 데이터와 백업을 찾을 수 없습니다.";
            }
            catch (Exception failure) { error = "저장 데이터에 접근하지 못했습니다. " + failure.Message; }
            return false;
        }
    }
}
