using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Writes a flushed sibling file and atomically replaces the primary, retaining the last valid backup.</summary>
public sealed class PlayerUpgradeFileStore : IPlayerUpgradeStore
{
    private readonly string _path;
    private readonly string _backupPath;
    private bool _loadedFromBackup;
    private readonly EngravingSettingsSO _engravings;

    public PlayerUpgradeFileStore(string path, EngravingSettingsSO engravings = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A save path is required.", nameof(path));
        _path = Path.GetFullPath(path);
        _backupPath = _path + ".bak";
        _engravings = engravings;
    }

    public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
    {
        profile = null;
        error = null;
        _loadedFromBackup = false;
        bool primaryKnown = Probe(_path, out bool primaryExists, out string primaryError);
        bool backupKnown = Probe(_backupPath, out bool backupExists, out string backupError);
        if (primaryKnown && backupKnown && !primaryExists && !backupExists)
        { profile = PlayerUpgradeProfile.New(); return true; }
        if (primaryExists && Read(_path, out profile, out primaryError)) return true;
        if (backupExists && Read(_backupPath, out profile, out backupError))
        {
            _loadedFromBackup = true;
            return true;
        }
        profile = null;
        error = "강화 저장 데이터를 불러오지 못했습니다. 기존 파일은 보존됩니다. "
            + (primaryError ?? backupError ?? "주 저장 파일이 없습니다.");
        return false;
    }

    private static bool Probe(string path, out bool exists, out string error)
    {
        exists = false;
        error = null;
        try { File.GetAttributes(path); exists = true; return true; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (Exception failure) { error = failure.Message; return false; }
    }

    private bool Read(string path, out PlayerUpgradeProfile profile, out string error)
    {
        profile = null;
        error = null;
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            // JsonUtility otherwise accepts missing scalar fields as zero, disguising a truncated profile.
            foreach (string field in new[] { "version", "currency", "healthLevel", "attackLevel", "defenseLevel" })
                if (!Regex.IsMatch(json, "\"" + field + "\"\\s*:"))
                    throw new InvalidDataException("필수 저장 항목이 없습니다: " + field);
            profile = JsonUtility.FromJson<PlayerUpgradeProfile>(json);
            if (profile != null && profile.version == 2 && !Regex.IsMatch(json, "\"unlockedNodeIds\"\\s*:"))
                throw new InvalidDataException("필수 저장 항목이 없습니다: unlockedNodeIds");
            if (profile == null || !profile.Validate(out error))
                throw new InvalidDataException(error ?? "저장 데이터가 비어 있습니다.");
            var definitions = _engravings != null ? _engravings : EngravingSettingsSO.Default;
            var owned = new HashSet<string>(profile.unlockedNodeIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (string id in owned)
            {
                var node = definitions.Find(id);
                if (node == null || node.parentId != "origin" && !owned.Contains(node.parentId))
                    throw new InvalidDataException("저장된 각인 또는 선행 조건이 유효하지 않습니다: " + id);
            }
            return true;
        }
        catch (Exception failure)
        {
            profile = null;
            error = failure.Message;
            return false;
        }
    }

    public bool TrySave(PlayerUpgradeProfile profile, out string error)
    {
        error = null;
        if (profile == null || !profile.Validate(out error))
        { error ??= "강화 저장 데이터가 없습니다."; return false; }
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(profile, true));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(_path))
                File.Replace(temporary, _path, _loadedFromBackup ? null : _backupPath);
            else File.Move(temporary, _path);
            _loadedFromBackup = false;
            return true;
        }
        catch (Exception failure)
        {
            error = "강화 데이터를 저장하지 못했습니다. " + failure.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception) { /* A leftover temporary file never replaces a valid profile. */ }
        }
    }
}
