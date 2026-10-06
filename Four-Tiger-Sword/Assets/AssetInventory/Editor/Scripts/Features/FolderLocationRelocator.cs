using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.PackageManager;

namespace AssetInventory
{
    internal static class FolderLocationRelocator
    {
        internal enum MappingMode
        {
            PreserveStructure,
            CollapseBoundaryOverlap
        }

        internal enum Operation
        {
            ChangeExistingLocation,
            MoveMissingLocation,
            RenameOnDisk
        }

        internal sealed class Plan
        {
            internal string OldRoot;
            internal string NewRoot;
            internal MappingMode Mode;
            internal readonly List<FolderChange> FolderChanges = new List<FolderChange>();
            internal readonly List<RelativeLocationChange> RelativeLocationChanges = new List<RelativeLocationChange>();
            internal readonly List<AssetChange> AssetChanges = new List<AssetChange>();
            internal readonly List<AssetFileChange> AssetFileChanges = new List<AssetFileChange>();
        }

        internal sealed class FolderChange
        {
            internal FolderSpec Spec;
            internal string OldLocation;
            internal bool OldStoreRelative;
            internal string OldRelativeKey;
            internal string NewLocation;
            internal bool NewStoreRelative;
            internal string NewRelativeKey;
        }

        internal sealed class RelativeLocationChange
        {
            internal int ExistingId;
            internal string Key;
            internal string System;
            internal string NewLocation;
        }

        internal sealed class AssetChange
        {
            internal int Id;
            internal string NewLocation;
            internal string NewSafeName;
        }

        internal sealed class AssetFileChange
        {
            internal int Id;
            internal string NewPath;
            internal string NewSourcePath;
        }

        private sealed class RelativePersistenceTarget
        {
            internal string Key;
            internal string Location;
        }

        private sealed class MappedFolder
        {
            internal FolderSpec Spec;
            internal string Target;
            internal string RelativeKey;
        }

        private sealed class CandidateData
        {
            internal readonly Dictionary<int, Asset> Assets = new Dictionary<int, Asset>();
            internal readonly Dictionary<int, AssetFile> AssetFiles = new Dictionary<int, AssetFile>();
            internal readonly Dictionary<int, Asset> Owners = new Dictionary<int, Asset>();
            internal readonly List<string> PhysicalPaths = new List<string>();
        }

        internal static bool TryGetRelocationGroup(FolderSpec primary, bool includeDescendants, out List<FolderSpec> group, out string error)
        {
            group = new List<FolderSpec>();
            error = null;
            if (primary == null)
            {
                error = "No source folder was selected.";
                return false;
            }

            try
            {
                Dictionary<string, RelativeLocation> mappings = LoadCurrentRelativeLocations(out error);
                if (mappings == null) return false;

                string oldRoot = ResolveStoredPath(primary.location, mappings);
                oldRoot = Paths.NormalizePathForComparison(oldRoot);
                if (string.IsNullOrWhiteSpace(oldRoot))
                {
                    error = "The current source location could not be resolved.";
                    return false;
                }

                foreach (FolderSpec spec in AI.Config.folders)
                {
                    if (spec == null) continue;
                    string location = Paths.NormalizePathForComparison(ResolveStoredPath(spec.location, mappings));
                    if (string.IsNullOrWhiteSpace(location)) continue;

                    bool exact = Paths.AreEquivalentPaths(location, oldRoot);
                    if (exact || includeDescendants && Paths.IsSameOrChildPath(location, oldRoot)) group.Add(spec);
                }

                if (!group.Contains(primary)) group.Insert(0, primary);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not inspect the configured source folders: {exception.Message}";
                return false;
            }
        }

        internal static bool TryCreatePlan(
            FolderSpec primary,
            string newRoot,
            IReadOnlyCollection<FolderSpec> movingSpecs,
            Operation operation,
            out Plan plan,
            out string error,
            Func<string, bool> pathExists = null,
            Func<string, bool> directoryExists = null)
        {
            plan = null;
            error = null;
            pathExists = pathExists ?? ExistsOnDisk;
            directoryExists = directoryExists ?? Directory.Exists;

            if (primary == null || movingSpecs == null || movingSpecs.Count == 0)
            {
                error = "No source folders were selected for relocation.";
                return false;
            }

            try
            {
                Dictionary<string, RelativeLocation> mappings = LoadCurrentRelativeLocations(out error);
                if (mappings == null) return false;

                string oldRoot = Paths.NormalizePathForComparison(ResolveStoredPath(primary.location, mappings));
                string normalizedNewRoot = Paths.NormalizePathForComparison(newRoot);
                if (string.IsNullOrWhiteSpace(oldRoot) || string.IsNullOrWhiteSpace(normalizedNewRoot))
                {
                    error = "The old or new source location could not be resolved.";
                    return false;
                }

                HashSet<FolderSpec> movingSet = new HashSet<FolderSpec>(movingSpecs);
                if (!movingSet.Contains(primary))
                {
                    error = "The selected source folder is missing from the relocation set.";
                    return false;
                }

                foreach (FolderSpec spec in movingSet)
                {
                    string location = Paths.NormalizePathForComparison(ResolveStoredPath(spec.location, mappings));
                    if (!Paths.IsSameOrChildPath(location, oldRoot))
                    {
                        error = $"The source '{location}' is outside the folder being relocated.";
                        return false;
                    }
                }

                if (!ValidateOwnershipBoundaries(oldRoot, normalizedNewRoot, movingSet, mappings, operation, out error)) return false;

                HashSet<int> folderTypes = GetAffectedFolderTypes(oldRoot, movingSet, mappings, operation);
                CandidateData candidates = LoadCandidates(oldRoot, folderTypes, mappings);
                foreach (FolderSpec spec in movingSet)
                {
                    string location = ResolveStoredPath(spec.location, mappings);
                    if (!string.IsNullOrWhiteSpace(location)) candidates.PhysicalPaths.Add(location);
                }

                bool verifyDestination = operation != Operation.RenameOnDisk;
                MappingMode mode = MappingMode.PreserveStructure;
                if (verifyDestination && !TryDetermineMappingMode(candidates.PhysicalPaths, oldRoot, normalizedNewRoot, pathExists, out mode, out error)) return false;

                Plan result = new Plan
                {
                    OldRoot = oldRoot,
                    NewRoot = normalizedNewRoot,
                    Mode = mode
                };

                List<MappedFolder> mappedFolders = new List<MappedFolder>();
                foreach (FolderSpec spec in movingSet)
                {
                    string location = ResolveStoredPath(spec.location, mappings);
                    if (!TryMapPath(location, oldRoot, normalizedNewRoot, mode, out string target))
                    {
                        error = $"The source '{location}' could not be mapped below the selected folder.";
                        return false;
                    }

                    if (verifyDestination && !directoryExists(GetFilesystemPath(target)))
                    {
                        error = $"The selected folder does not contain the configured nested source '{target}'.";
                        return false;
                    }

                    mappedFolders.Add(new MappedFolder
                    {
                        Spec = spec,
                        Target = target,
                        RelativeKey = GetRelativeKey(spec.location)
                    });
                }

                if (!BuildFolderAndRelativeChanges(result, mappedFolders, movingSet, mappings, candidates, out Dictionary<string, RelativePersistenceTarget> relativeTargets, out error)) return false;
                BuildAssetChanges(result, candidates, mappings, relativeTargets);

                plan = result;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not prepare the folder relocation: {exception.Message}";
                return false;
            }
        }

        internal static bool TryApply(Plan plan, Func<bool> saveConfig, out string error)
        {
            error = null;
            if (plan == null)
            {
                error = "No folder relocation was prepared.";
                return false;
            }

            saveConfig = saveConfig ?? AI.TrySaveConfig;
            bool configApplied = false;
            try
            {
                DBAdapter.DB.RunInTransaction(() =>
                {
                    foreach (RelativeLocationChange change in plan.RelativeLocationChanges)
                    {
                        if (change.ExistingId > 0)
                        {
                            DBAdapter.DB.Execute("UPDATE RelativeLocation SET Location=? WHERE Id=?", change.NewLocation, change.ExistingId);
                        }
                        else
                        {
                            RelativeLocation location = new RelativeLocation(change.Key, change.System, change.NewLocation);
                            DBAdapter.DB.Insert(location);
                        }
                    }

                    foreach (AssetChange change in plan.AssetChanges)
                    {
                        DBAdapter.DB.Execute("UPDATE Asset SET Location=?, SafeName=? WHERE Id=?", change.NewLocation, change.NewSafeName, change.Id);
                    }

                    foreach (AssetFileChange change in plan.AssetFileChanges)
                    {
                        DBAdapter.DB.Execute("UPDATE AssetFile SET Path=?, SourcePath=? WHERE Id=?", change.NewPath, change.NewSourcePath, change.Id);
                    }

                    foreach (FolderChange change in plan.FolderChanges)
                    {
                        change.Spec.location = change.NewLocation;
                        change.Spec.storeRelative = change.NewStoreRelative;
                        change.Spec.relativeKey = change.NewRelativeKey;
                    }

                    configApplied = true;
                    if (!saveConfig()) throw new InvalidOperationException("The configuration file could not be saved.");
                    Paths.LoadRelativeLocations();
                });

                return true;
            }
            catch (Exception exception)
            {
                foreach (FolderChange change in plan.FolderChanges)
                {
                    change.Spec.location = change.OldLocation;
                    change.Spec.storeRelative = change.OldStoreRelative;
                    change.Spec.relativeKey = change.OldRelativeKey;
                }

                bool restored = true;
                if (configApplied)
                {
                    try
                    {
                        restored = saveConfig();
                    }
                    catch (Exception restoreException)
                    {
                        restored = false;
                        error = $"{exception.Message} Restoring the previous configuration also failed: {restoreException.Message}";
                    }
                }

                string reloadError = null;
                try
                {
                    Paths.LoadRelativeLocations();
                }
                catch (Exception reloadException)
                {
                    reloadError = reloadException.Message;
                }

                if (error == null)
                {
                    error = restored
                        ? exception.Message
                        : $"{exception.Message} The previous configuration could not be persisted again.";
                }
                if (!string.IsNullOrWhiteSpace(reloadError)) error += $" Reloading the previous Relative Storage mappings also failed: {reloadError}";
                return false;
            }
        }

        private static bool ValidateOwnershipBoundaries(
            string oldRoot,
            string newRoot,
            HashSet<FolderSpec> movingSpecs,
            Dictionary<string, RelativeLocation> mappings,
            Operation operation,
            out string error)
        {
            error = null;
            HashSet<int> movingTypes = new HashSet<int>(movingSpecs.Select(spec => spec.folderType));
            foreach (FolderSpec spec in AI.Config.folders)
            {
                if (spec == null || movingSpecs.Contains(spec) || !movingTypes.Contains(spec.folderType)) continue;

                string location = Paths.NormalizePathForComparison(ResolveStoredPath(spec.location, mappings));
                if (string.IsNullOrWhiteSpace(location)) continue;

                if (Paths.IsSameOrChildPath(location, oldRoot))
                {
                    error = $"Another {GetFolderTypeName(spec.folderType)} source uses the same or a nested location. Move the complete source group so its indexed data remains unambiguous.";
                    return false;
                }

                if (Paths.IsSameOrChildPath(oldRoot, location)
                    && (operation == Operation.ChangeExistingLocation
                        || operation == Operation.RenameOnDisk && !Paths.IsSameOrChildPath(newRoot, location)))
                {
                    error = $"The folder is also covered by the parent {GetFolderTypeName(spec.folderType)} source '{location}', so its indexed data cannot be separated while the old folder still exists. Remove the overlapping source setting or relocate the complete source first.";
                    return false;
                }
            }

            return true;
        }

        private static HashSet<int> GetAffectedFolderTypes(
            string oldRoot,
            HashSet<FolderSpec> movingSpecs,
            Dictionary<string, RelativeLocation> mappings,
            Operation operation)
        {
            HashSet<int> result = new HashSet<int>(movingSpecs.Select(spec => spec.folderType));
            if (operation != Operation.RenameOnDisk) return result;

            foreach (FolderSpec spec in AI.Config.folders)
            {
                if (spec == null || movingSpecs.Contains(spec)) continue;
                string location = Paths.NormalizePathForComparison(ResolveStoredPath(spec.location, mappings));
                if (Paths.IsSameOrChildPath(oldRoot, location)) result.Add(spec.folderType);
            }

            return result;
        }

        private static string GetFolderTypeName(int folderType)
        {
            switch (folderType)
            {
                case 0: return "Unity Packages";
                case 1: return "Media Files";
                case 2: return "Archives";
                case 3: return "Dev Packages";
                default: return "Additional Folder";
            }
        }

        private static CandidateData LoadCandidates(string oldRoot, HashSet<int> folderTypes, Dictionary<string, RelativeLocation> mappings)
        {
            CandidateData result = new CandidateData();
            HashSet<string> prefixes = new HashSet<string>(StringComparer.Ordinal) {oldRoot};
            foreach (KeyValuePair<string, RelativeLocation> mapping in mappings)
            {
                string location = Paths.NormalizePathForComparison(mapping.Value.Location);
                if (string.IsNullOrWhiteSpace(location)) continue;
                if (Paths.IsSameOrChildPath(location, oldRoot) || Paths.IsSameOrChildPath(oldRoot, location))
                {
                    prefixes.Add(AI.TAG_START + mapping.Key + AI.TAG_END);
                }
            }

            foreach (string prefix in prefixes)
            {
                string likePrefix = EscapeLikePattern(prefix) + "%";
                List<Asset> assets = DBAdapter.DB.Query<Asset>(
                    "SELECT Id, ParentId, AssetSource, PackageSource, Location, SafeName FROM Asset WHERE Location LIKE ? ESCAPE '\\' OR SafeName LIKE ? ESCAPE '\\'",
                    likePrefix,
                    likePrefix);
                foreach (Asset asset in assets)
                {
                    if (!IsOwnedByFolderTypes(asset, folderTypes)) continue;
                    result.Assets[asset.Id] = asset;
                    result.Owners[asset.Id] = asset;
                }

                List<AssetFile> files = DBAdapter.DB.Query<AssetFile>(
                    "SELECT Id, AssetId, Path, SourcePath FROM AssetFile WHERE Path LIKE ? ESCAPE '\\' OR SourcePath LIKE ? ESCAPE '\\'",
                    likePrefix,
                    likePrefix);
                foreach (AssetFile file in files) result.AssetFiles[file.Id] = file;
            }

            foreach (Asset asset in result.Assets.Values)
            {
                AddPhysicalPath(asset.Location, oldRoot, mappings, result.PhysicalPaths);
                AddPhysicalPath(asset.SafeName, oldRoot, mappings, result.PhysicalPaths);
            }

            List<int> rejectedFileIds = new List<int>();
            foreach (AssetFile file in result.AssetFiles.Values)
            {
                if (!result.Owners.TryGetValue(file.AssetId, out Asset owner))
                {
                    owner = DBAdapter.DB.Find<Asset>(file.AssetId);
                    if (owner != null) result.Owners[file.AssetId] = owner;
                }

                if (!IsOwnedByFolderTypes(owner, folderTypes))
                {
                    rejectedFileIds.Add(file.Id);
                    continue;
                }

                AddPhysicalPath(file.Path, oldRoot, mappings, result.PhysicalPaths);
                AddPhysicalPath(file.SourcePath, oldRoot, mappings, result.PhysicalPaths);
            }

            foreach (int rejectedId in rejectedFileIds) result.AssetFiles.Remove(rejectedId);
            return result;
        }

        private static void AddPhysicalPath(string storedPath, string oldRoot, Dictionary<string, RelativeLocation> mappings, List<string> paths)
        {
            string physicalPath = ResolveStoredPath(storedPath, mappings);
            string normalized = Paths.NormalizePathForComparison(physicalPath);
            if (Paths.IsSameOrChildPath(GetFilesystemPath(normalized), oldRoot)) paths.Add(normalized);
        }

        private static bool TryDetermineMappingMode(
            IEnumerable<string> physicalPaths,
            string oldRoot,
            string newRoot,
            Func<string, bool> pathExists,
            out MappingMode mode,
            out string error)
        {
            mode = MappingMode.PreserveStructure;
            error = null;
            bool descendantCandidate = false;
            bool nonOverlapEvidence = false;
            bool overlapPossible = false;
            bool standardEvidence = false;
            bool collapsedEvidence = false;

            foreach (string path in physicalPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal))
            {
                if (!Paths.TryRebaseUnderRoot(path, oldRoot, newRoot, out string standardPath)) continue;
                if (!Paths.TryGetRelativePath(GetFilesystemPath(path), oldRoot, out string relativePath) || string.IsNullOrWhiteSpace(relativePath)) continue;

                descendantCandidate = true;
                bool hasOverlap = Paths.TryRebaseUnderRoot(path, oldRoot, newRoot, true, out string collapsedPath, out int collapsedSegments)
                    && collapsedSegments > 0
                    && !Paths.AreEquivalentPaths(standardPath, collapsedPath);
                if (!hasOverlap)
                {
                    if (!nonOverlapEvidence && pathExists(GetFilesystemPath(standardPath))) nonOverlapEvidence = true;
                    continue;
                }

                overlapPossible = true;
                bool standardExists = pathExists(GetFilesystemPath(standardPath));
                bool collapsedExists = pathExists(GetFilesystemPath(collapsedPath));
                if (standardExists)
                {
                    standardEvidence = true;
                }
                else if (collapsedExists)
                {
                    collapsedEvidence = true;
                }
            }

            if (standardEvidence && collapsedEvidence)
            {
                error = "The selected folder contains conflicting layouts for the indexed source. No paths were changed.";
                return false;
            }

            if (standardEvidence) return true;
            if (collapsedEvidence)
            {
                mode = MappingMode.CollapseBoundaryOverlap;
                return true;
            }

            if (overlapPossible)
            {
                error = "The selected folder does not contain enough of the indexed source to verify its path layout. No paths were changed.";
                return false;
            }

            if (descendantCandidate && !nonOverlapEvidence)
            {
                error = "The selected folder does not contain any indexed content expected for this source. No paths were changed.";
                return false;
            }

            return true;
        }

        private static bool BuildFolderAndRelativeChanges(
            Plan plan,
            List<MappedFolder> mappedFolders,
            HashSet<FolderSpec> movingSpecs,
            Dictionary<string, RelativeLocation> mappings,
            CandidateData candidates,
            out Dictionary<string, RelativePersistenceTarget> relativeTargets,
            out string error)
        {
            relativeTargets = new Dictionary<string, RelativePersistenceTarget>(StringComparer.Ordinal);
            error = null;
            HashSet<string> usedKeys = new HashSet<string>(DBAdapter.DB.QueryScalars<string>("SELECT DISTINCT `Key` FROM RelativeLocation"), StringComparer.Ordinal);
            foreach (FolderSpec spec in AI.Config.folders)
            {
                string key = GetRelativeKey(spec?.location);
                if (!string.IsNullOrWhiteSpace(key)) usedKeys.Add(key);
            }

            foreach (IGrouping<string, MappedFolder> relativeGroup in mappedFolders.Where(folder => !string.IsNullOrWhiteSpace(folder.RelativeKey)).GroupBy(folder => folder.RelativeKey))
            {
                string oldKey = relativeGroup.Key;
                if (oldKey == "ac" || oldKey == "pc")
                {
                    error = "Reserved cache mappings cannot be used as Additional Folder locations.";
                    return false;
                }

                List<FolderSpec> allKeyUsers = AI.Config.folders.Where(spec => string.Equals(GetRelativeKey(spec?.location), oldKey, StringComparison.Ordinal)).ToList();
                bool allUsersMoving = allKeyUsers.All(movingSpecs.Contains) && CanRetargetRelativeLocation(oldKey, plan.OldRoot, mappings, candidates);
                string newKey = allUsersMoving ? oldKey : CreateUniqueKey(oldKey, usedKeys);
                usedKeys.Add(newKey);

                RelativeLocation existing = mappings.TryGetValue(oldKey, out RelativeLocation mapping) ? mapping : null;
                string mappingTarget = plan.NewRoot;
                if (allUsersMoving && existing != null && TryMapPath(existing.Location, plan.OldRoot, plan.NewRoot, plan.Mode, out string mappedLocation))
                {
                    mappingTarget = mappedLocation;
                }

                mappingTarget = Paths.NormalizePathForComparison(mappingTarget);
                RelativeLocation targetRecord = allUsersMoving ? existing : null;
                plan.RelativeLocationChanges.Add(new RelativeLocationChange
                {
                    ExistingId = targetRecord?.Id ?? 0,
                    Key = newKey,
                    System = AI.GetSystemId(),
                    NewLocation = mappingTarget
                });
                relativeTargets[oldKey] = new RelativePersistenceTarget {Key = newKey, Location = mappingTarget};

                foreach (MappedFolder folder in relativeGroup)
                {
                    if (!TryMakeRelative(folder.Target, newKey, mappingTarget, out string storedTarget))
                    {
                        error = $"The relative source '{folder.Target}' could not be stored below mapping '{newKey}'.";
                        return false;
                    }

                    plan.FolderChanges.Add(CreateFolderChange(folder.Spec, storedTarget, true, newKey));
                }
            }

            foreach (MappedFolder folder in mappedFolders.Where(folder => string.IsNullOrWhiteSpace(folder.RelativeKey)))
            {
                plan.FolderChanges.Add(CreateFolderChange(folder.Spec, Paths.NormalizePathForComparison(folder.Target), false, null));
            }

            return true;
        }

        private static bool CanRetargetRelativeLocation(
            string key,
            string oldRoot,
            Dictionary<string, RelativeLocation> mappings,
            CandidateData candidates)
        {
            string prefix = EscapeLikePattern(AI.TAG_START + key + AI.TAG_END) + "%";
            List<Asset> assets = DBAdapter.DB.Query<Asset>(
                "SELECT Id, Location, SafeName FROM Asset WHERE Location LIKE ? ESCAPE '\\' OR SafeName LIKE ? ESCAPE '\\'",
                prefix,
                prefix);
            foreach (Asset asset in assets)
            {
                bool isCandidate = candidates.Assets.ContainsKey(asset.Id);
                if (!CanMoveRelativeValue(asset.Location, key, oldRoot, mappings, isCandidate)
                    || !CanMoveRelativeValue(asset.SafeName, key, oldRoot, mappings, isCandidate)) return false;
            }

            List<AssetFile> files = DBAdapter.DB.Query<AssetFile>(
                "SELECT Id, Path, SourcePath FROM AssetFile WHERE Path LIKE ? ESCAPE '\\' OR SourcePath LIKE ? ESCAPE '\\'",
                prefix,
                prefix);
            foreach (AssetFile file in files)
            {
                bool isCandidate = candidates.AssetFiles.ContainsKey(file.Id);
                if (!CanMoveRelativeValue(file.Path, key, oldRoot, mappings, isCandidate)
                    || !CanMoveRelativeValue(file.SourcePath, key, oldRoot, mappings, isCandidate)) return false;
            }

            return true;
        }

        private static bool CanMoveRelativeValue(
            string storedValue,
            string key,
            string oldRoot,
            Dictionary<string, RelativeLocation> mappings,
            bool isCandidate)
        {
            if (!string.Equals(GetRelativeKey(storedValue), key, StringComparison.Ordinal)) return true;
            if (!isCandidate) return false;

            string physicalPath = Paths.NormalizePathForComparison(ResolveStoredPath(storedValue, mappings));
            return Paths.IsSameOrChildPath(GetFilesystemPath(physicalPath), oldRoot);
        }

        private static FolderChange CreateFolderChange(FolderSpec spec, string newLocation, bool storeRelative, string relativeKey)
        {
            return new FolderChange
            {
                Spec = spec,
                OldLocation = spec.location,
                OldStoreRelative = spec.storeRelative,
                OldRelativeKey = spec.relativeKey,
                NewLocation = newLocation,
                NewStoreRelative = storeRelative,
                NewRelativeKey = relativeKey
            };
        }

        private static void BuildAssetChanges(
            Plan plan,
            CandidateData candidates,
            Dictionary<string, RelativeLocation> mappings,
            Dictionary<string, RelativePersistenceTarget> relativeTargets)
        {
            foreach (Asset asset in candidates.Assets.Values)
            {
                string location = MapStoredValue(asset.Location, plan, mappings, relativeTargets);
                string safeName = MapStoredValue(asset.SafeName, plan, mappings, relativeTargets);
                if (string.Equals(location, asset.Location, StringComparison.Ordinal) && string.Equals(safeName, asset.SafeName, StringComparison.Ordinal)) continue;

                plan.AssetChanges.Add(new AssetChange
                {
                    Id = asset.Id,
                    NewLocation = location,
                    NewSafeName = safeName
                });
            }

            foreach (AssetFile file in candidates.AssetFiles.Values)
            {
                string path = MapStoredValue(file.Path, plan, mappings, relativeTargets);
                string sourcePath = MapStoredValue(file.SourcePath, plan, mappings, relativeTargets);
                if (string.Equals(path, file.Path, StringComparison.Ordinal) && string.Equals(sourcePath, file.SourcePath, StringComparison.Ordinal)) continue;

                plan.AssetFileChanges.Add(new AssetFileChange
                {
                    Id = file.Id,
                    NewPath = path,
                    NewSourcePath = sourcePath
                });
            }
        }

        private static string MapStoredValue(
            string storedValue,
            Plan plan,
            Dictionary<string, RelativeLocation> mappings,
            Dictionary<string, RelativePersistenceTarget> relativeTargets)
        {
            if (string.IsNullOrWhiteSpace(storedValue)) return storedValue;
            string physicalPath = ResolveStoredPath(storedValue, mappings);
            if (!TryMapPath(physicalPath, plan.OldRoot, plan.NewRoot, plan.Mode, out string mappedPath)) return storedValue;

            string oldKey = GetRelativeKey(storedValue);
            if (!string.IsNullOrWhiteSpace(oldKey) && relativeTargets.TryGetValue(oldKey, out RelativePersistenceTarget target)
                && TryMakeRelative(mappedPath, target.Key, target.Location, out string relativePath))
            {
                return relativePath;
            }

            if (!string.IsNullOrWhiteSpace(oldKey) && !relativeTargets.ContainsKey(oldKey)
                && mappings.TryGetValue(oldKey, out RelativeLocation existing)
                && TryMakeRelative(mappedPath, oldKey, existing.Location, out string preservedRelativePath))
            {
                return preservedRelativePath;
            }

            return Paths.NormalizePathForComparison(mappedPath);
        }

        private static bool TryMapPath(string path, string oldRoot, string newRoot, MappingMode mode, out string mappedPath)
        {
            if (mode == MappingMode.CollapseBoundaryOverlap)
            {
                return Paths.TryRebaseUnderRoot(path, oldRoot, newRoot, true, out mappedPath, out _);
            }

            return Paths.TryRebaseUnderRoot(path, oldRoot, newRoot, out mappedPath);
        }

        private static bool TryMakeRelative(string physicalPath, string key, string mappingRoot, out string relativePath)
        {
            relativePath = null;
            Paths.SplitStoredPath(physicalPath, out string filesystemPath, out string storedSuffix);
            if (!Paths.TryGetRelativePath(filesystemPath, mappingRoot, out string suffix)) return false;

            relativePath = AI.TAG_START + key + AI.TAG_END;
            if (!string.IsNullOrEmpty(suffix)) relativePath += "/" + suffix;
            relativePath += storedSuffix;
            return true;
        }

        private static Dictionary<string, RelativeLocation> LoadCurrentRelativeLocations(out string error)
        {
            error = null;
            string systemId = AI.GetSystemId();
            List<RelativeLocation> rows = DBAdapter.DB.Query<RelativeLocation>("SELECT * FROM RelativeLocation WHERE System=?", systemId);
            Dictionary<string, RelativeLocation> result = new Dictionary<string, RelativeLocation>(StringComparer.Ordinal);
            foreach (RelativeLocation row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Key)) continue;
                if (result.ContainsKey(row.Key))
                {
                    error = $"Relative Storage contains more than one mapping for key '{row.Key}' on this system.";
                    return null;
                }

                result.Add(row.Key, row);
            }

            return result;
        }

        private static string ResolveStoredPath(string storedPath, Dictionary<string, RelativeLocation> mappings)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return storedPath;
            Paths.SplitStoredPath(storedPath, out string filesystemPath, out string storedSuffix);
            if (!TrySplitRelativePath(filesystemPath, out string key, out string relativeSuffix)) return Paths.NormalizePathForComparison(storedPath);
            if (!mappings.TryGetValue(key, out RelativeLocation mapping) || string.IsNullOrWhiteSpace(mapping.Location)) return storedPath;

            string root = Paths.NormalizePathForComparison(mapping.Location);
            string resolved = root;
            if (!string.IsNullOrWhiteSpace(relativeSuffix))
            {
                string suffix = relativeSuffix.TrimStart('/');
                resolved = root.EndsWith("/", StringComparison.Ordinal) ? root + suffix : root + "/" + suffix;
            }
            return resolved + storedSuffix;
        }

        private static string GetRelativeKey(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return null;
            Paths.SplitStoredPath(storedPath, out string filesystemPath, out _);
            return TrySplitRelativePath(filesystemPath, out string key, out _) ? key : null;
        }

        private static bool TrySplitRelativePath(string path, out string key, out string suffix)
        {
            key = null;
            suffix = null;
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(AI.TAG_START, StringComparison.Ordinal)) return false;

            int endIndex = path.IndexOf(AI.TAG_END, AI.TAG_START.Length, StringComparison.Ordinal);
            if (endIndex <= AI.TAG_START.Length) return false;

            key = path.Substring(AI.TAG_START.Length, endIndex - AI.TAG_START.Length);
            suffix = path.Substring(endIndex + AI.TAG_END.Length);
            return true;
        }

        private static bool IsOwnedByFolderTypes(Asset asset, HashSet<int> folderTypes)
        {
            if (asset == null) return false;
            foreach (int folderType in folderTypes)
            {
                switch (folderType)
                {
                    case 0:
                        if (asset.AssetSource == Asset.Source.CustomPackage || asset.AssetSource == Asset.Source.AssetStorePackage) return true;
                        break;
                    case 1:
                        if (asset.AssetSource == Asset.Source.Directory) return true;
                        break;
                    case 2:
                        if (asset.AssetSource == Asset.Source.Archive) return true;
                        break;
                    case 3:
                        if (asset.AssetSource == Asset.Source.RegistryPackage && asset.PackageSource == PackageSource.Local) return true;
                        break;
                }
            }

            return false;
        }

        private static string CreateUniqueKey(string baseKey, HashSet<string> usedKeys)
        {
            int suffix = 2;
            string candidate;
            do
            {
                candidate = baseKey + "-" + suffix;
                suffix++;
            } while (usedKeys.Contains(candidate));

            return candidate;
        }

        private static string EscapeLikePattern(string value)
        {
            return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private static string GetFilesystemPath(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return storedPath;
            Paths.SplitStoredPath(storedPath, out string filesystemPath, out _);
            return filesystemPath;
        }

        private static bool ExistsOnDisk(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }
    }
}
