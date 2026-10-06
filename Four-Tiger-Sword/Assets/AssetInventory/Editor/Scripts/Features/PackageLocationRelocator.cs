using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace AssetInventory
{
    internal static class PackageLocationRelocator
    {
        private static readonly string[] SidecarSuffixes =
        {
            ".icon.png",
            ".info.json",
            ".overrides.json"
        };

        private static readonly char[] PortableInvalidFileNameChars =
        {
            '<',
            '>',
            ':',
            (char)34,
            '/',
            '\\',
            '|',
            '?',
            '*'
        };

        private static readonly string[] ReservedWindowsNames =
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9"
        };

        internal sealed class Plan
        {
            internal int RootId;
            internal string OldRoot;
            internal string NewRoot;
            internal readonly List<LocationChange> Changes = new List<LocationChange>();
        }

        internal sealed class LocationChange
        {
            internal int Id;
            internal string OldLocation;
            internal string NewLocation;
        }

        private sealed class FileMove
        {
            internal string Source;
            internal string Destination;
            internal bool UseAssetDatabase;
        }

        private sealed class MoveLeg
        {
            internal string Source;
            internal string Destination;
            internal bool UseAssetDatabase;
        }

        internal static bool SupportsSetLocation(Asset asset)
        {
            if (asset == null || asset.ParentId > 0) return false;

            switch (asset.AssetSource)
            {
                case Asset.Source.AssetStorePackage:
                case Asset.Source.CustomPackage:
                case Asset.Source.Archive:
                case Asset.Source.Directory:
                    return true;

                case Asset.Source.RegistryPackage:
                    return asset.PackageSource == UnityEditor.PackageManager.PackageSource.Embedded
                        || asset.PackageSource == UnityEditor.PackageManager.PackageSource.Local
                        || asset.PackageSource == UnityEditor.PackageManager.PackageSource.LocalTarball;

                default:
                    return false;
            }
        }

        internal static bool SupportsRenameFile(Asset asset)
        {
            return asset != null
                && asset.ParentId <= 0
                && (asset.AssetSource == Asset.Source.CustomPackage || asset.AssetSource == Asset.Source.Archive);
        }

        internal static bool CanRenameFile(Asset asset, out string reason, Func<Asset, bool> activeIndexing = null)
        {
            reason = null;
            if (!TryLoadPersistedRoot(asset, out Asset persistedRoot, out string loadCause))
            {
                reason = loadCause + " Use Set Location after restoring or reconnecting the package.";
                return false;
            }

            if (!TryValidateRenameSource(persistedRoot, activeIndexing, out _, out string cause, out string recovery))
            {
                reason = cause + " " + recovery;
                return false;
            }

            return true;
        }

        internal static bool TrySetLocation(Asset asset, string selectedPath, out string error, Func<Asset, bool> activeIndexing = null)
        {
            error = null;
            if (!TryLoadPersistedRoot(asset, out Asset persistedRoot, out string loadCause))
            {
                error = FormatFailure("The package location could not be updated.", loadCause, "Reload Asset Inventory and try again.");
                return false;
            }

            if (!SupportsSetLocation(persistedRoot))
            {
                error = FormatFailure(
                    "The package location could not be updated.",
                    "This package type does not support manual location changes.",
                    "Choose a top-level filesystem-backed package.");
                return false;
            }

            if (IsActiveIndexing(persistedRoot, activeIndexing))
            {
                error = FormatFailure(
                    "The package location could not be updated.",
                    "Package indexing is currently using this source type.",
                    "Wait for indexing to finish, then try again.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                error = FormatFailure(
                    "The package location could not be updated.",
                    "No destination was selected.",
                    "Select an existing package file or folder.");
                return false;
            }

            string normalizedPath = Paths.NormalizePathForComparison(selectedPath.Trim());
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                error = FormatFailure(
                    "The package location could not be updated.",
                    "The selected destination is not a valid filesystem path.",
                    "Select an existing package file or folder.");
                return false;
            }

            bool requiresFolder = RequiresFolderLocation(persistedRoot);
            if (requiresFolder && !Directory.Exists(normalizedPath))
            {
                error = FormatFailure(
                    "The package location could not be updated.",
                    $"The selected folder does not exist: '{normalizedPath}'.",
                    "Restore the folder or select an existing package folder.");
                return false;
            }
            if (!requiresFolder && !File.Exists(normalizedPath))
            {
                error = FormatFailure(
                    "The package location could not be updated.",
                    $"The selected file does not exist: '{normalizedPath}'.",
                    "Restore the file or select an existing package file.");
                return false;
            }

            string newStoredRoot = Paths.MakeRelative(normalizedPath);
            if (!TryCreatePlan(persistedRoot, newStoredRoot, out Plan plan, out string planCause))
            {
                error = FormatFailure(
                    "The package location could not be updated.",
                    planCause,
                    "Choose a location that is not owned by another catalog entry, then try again.");
                return false;
            }

            if (!TryApplyPlan(plan, out string persistenceCause))
            {
                error = FormatFailure(
                    "The package location could not be updated.",
                    persistenceCause,
                    "No files were moved. Resolve the database problem, reload Asset Inventory, and try again.");
                return false;
            }

            asset.Location = plan.NewRoot;
            return true;
        }

        internal static bool TryRenameFile(
            Asset asset,
            string newBaseName,
            out string error,
            Func<Asset, bool> activeIndexing = null,
            Func<Plan, string> persistPlan = null)
        {
            error = null;
            if (!TryLoadPersistedRoot(asset, out Asset persistedRoot, out string loadCause))
            {
                error = FormatFailure("The package file could not be renamed.", loadCause, "Reload Asset Inventory and try again.");
                return false;
            }

            if (!TryValidateRenameSource(persistedRoot, activeIndexing, out string sourcePath, out string sourceCause, out string sourceRecovery))
            {
                error = FormatFailure("The package file could not be renamed.", sourceCause, sourceRecovery);
                return false;
            }

            if (!TryValidateBaseName(newBaseName, out string validatedBaseName, out string nameCause, out string nameRecovery))
            {
                error = FormatFailure("The package file could not be renamed.", nameCause, nameRecovery);
                return false;
            }

            string extension = Path.GetExtension(sourcePath);
            string destinationPath = Paths.NormalizePathForComparison(Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty, validatedBaseName + extension));
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                error = FormatFailure(
                    "The package file could not be renamed.",
                    "The new filename could not be resolved in the source directory.",
                    "Enter a shorter valid filename and try again.");
                return false;
            }

            string normalizedSource = Paths.NormalizePathForComparison(sourcePath);
            if (string.Equals(normalizedSource, destinationPath, StringComparison.Ordinal))
            {
                error = FormatFailure(
                    "The package file could not be renamed.",
                    "The new filename is identical to the current filename.",
                    "Enter a different filename. The extension is preserved automatically.");
                return false;
            }

            string newStoredRoot = Paths.MakeRelative(destinationPath);
            if (!TryCreatePlan(persistedRoot, newStoredRoot, out Plan plan, out string planCause))
            {
                error = FormatFailure(
                    "The package file could not be renamed.",
                    planCause,
                    "Choose a filename that is not owned by another catalog entry, or repair the stale entry and try again.");
                return false;
            }

            if (!TryBuildFileMoves(sourcePath, destinationPath, out List<FileMove> moves, out string moveCause, out string moveRecovery))
            {
                error = FormatFailure("The package file could not be renamed.", moveCause, moveRecovery);
                return false;
            }

            List<MoveLeg> completedLegs = new List<MoveLeg>();
            foreach (FileMove move in moves)
            {
                if (TryExecuteFileMove(move, completedLegs, out moveCause)) continue;

                bool restored = TryRollbackMoves(completedLegs, out string rollbackCause);
                string recovery = restored
                    ? "Completed file moves were reversed. Resolve the reported filesystem problem and try again."
                    : $"Some file moves could not be reversed: {rollbackCause} Restore the original files manually, then use Set Location to reconnect the package.";
                error = FormatFailure("The package file could not be renamed.", moveCause, recovery);
                return false;
            }

            string persistenceCause;
            if (persistPlan == null)
            {
                TryApplyPlan(plan, out persistenceCause);
            }
            else
            {
                try
                {
                    persistenceCause = persistPlan(plan);
                }
                catch (Exception exception)
                {
                    persistenceCause = exception.Message;
                }
            }

            if (!string.IsNullOrWhiteSpace(persistenceCause))
            {
                bool restored = TryRollbackMoves(completedLegs, out string rollbackCause);
                string recovery = restored
                    ? "The file moves were reversed. Resolve the database problem, reload Asset Inventory, and try again."
                    : $"The catalog update failed and some file moves could not be reversed: {rollbackCause} Restore the original filenames manually, then use Set Location to reconnect the package.";
                error = FormatFailure("The package file could not be renamed.", persistenceCause, recovery);
                return false;
            }

            asset.Location = plan.NewRoot;
            return true;
        }

        private static bool TryLoadPersistedRoot(Asset asset, out Asset persistedRoot, out string cause)
        {
            persistedRoot = null;
            cause = null;
            if (asset == null)
            {
                cause = "No package is loaded.";
                return false;
            }
            if (asset.Id <= 0)
            {
                cause = "The package has not been persisted in the catalog.";
                return false;
            }

            try
            {
                persistedRoot = DBAdapter.DB.Find<Asset>(asset.Id);
            }
            catch (Exception exception)
            {
                cause = $"The latest catalog location could not be loaded: {exception.Message}";
                return false;
            }

            if (persistedRoot == null)
            {
                cause = "The package no longer exists in the catalog.";
                return false;
            }
            if (persistedRoot.ParentId > 0)
            {
                cause = "Only top-level package files can be relocated.";
                return false;
            }

            return true;
        }

        private static bool TryValidateRenameSource(
            Asset persistedRoot,
            Func<Asset, bool> activeIndexing,
            out string sourcePath,
            out string cause,
            out string recovery)
        {
            sourcePath = null;
            cause = null;
            recovery = null;
            if (!SupportsRenameFile(persistedRoot))
            {
                cause = "Rename File supports only top-level custom Unity packages and ZIP, RAR, or 7z archives.";
                recovery = "Rename this source externally, then use Set Location to reconnect it.";
                return false;
            }

            if (IsActiveIndexing(persistedRoot, activeIndexing))
            {
                cause = "Package indexing is currently using this source type.";
                recovery = "Wait for indexing to finish, then try again.";
                return false;
            }

            sourcePath = Paths.NormalizePathForComparison(persistedRoot.GetLocation(true));
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                cause = "The stored source location cannot be resolved on this system.";
                recovery = "Use Set Location to reconnect the package before renaming it.";
                return false;
            }
            if (!File.Exists(sourcePath))
            {
                cause = $"The source file is missing: '{sourcePath}'.";
                recovery = "Restore the file or use Set Location to reconnect it before renaming.";
                return false;
            }

            if (!TryDetectMultiPartArchive(sourcePath, out bool multiPart, out string inspectionCause))
            {
                cause = inspectionCause;
                recovery = "Check access to the source directory, then try again.";
                return false;
            }
            if (multiPart)
            {
                cause = "The source belongs to a multi-part archive.";
                recovery = "Rename every archive volume outside Asset Inventory, then use Set Location to reconnect the first volume.";
                return false;
            }

            string extension = Path.GetExtension(sourcePath);
            bool supported = persistedRoot.AssetSource == Asset.Source.CustomPackage
                ? string.Equals(extension, ".unitypackage", StringComparison.OrdinalIgnoreCase)
                : string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase);
            if (!supported)
            {
                cause = persistedRoot.AssetSource == Asset.Source.CustomPackage
                    ? "The custom package is not a .unitypackage file."
                    : "The archive is not a single-file ZIP, RAR, or 7z archive.";
                recovery = "Rename this source externally, then use Set Location to reconnect it.";
                return false;
            }

            return true;
        }

        private static bool IsActiveIndexing(Asset asset, Func<Asset, bool> activeIndexing)
        {
            if (activeIndexing != null) return activeIndexing(asset);
            if (AI.Actions == null) return false;

            AssetInfo info = new AssetInfo(asset)
            {
                Id = asset.Id,
                AssetId = asset.Id
            };
            return AI.Actions.IsPackageIndexingActionRunning(info);
        }

        private static bool TryValidateBaseName(string newBaseName, out string validatedBaseName, out string cause, out string recovery)
        {
            validatedBaseName = newBaseName;
            cause = null;
            recovery = "Enter a valid filename without a folder or extension change.";
            if (string.IsNullOrWhiteSpace(newBaseName))
            {
                cause = "The filename is empty.";
                return false;
            }
            if (!string.Equals(newBaseName, newBaseName.Trim(), StringComparison.Ordinal))
            {
                cause = "The filename starts or ends with whitespace.";
                return false;
            }
            if (newBaseName == "." || newBaseName == "..")
            {
                cause = "The filename is reserved for filesystem navigation.";
                return false;
            }
            if (newBaseName.EndsWith(".", StringComparison.Ordinal))
            {
                cause = "The filename ends with a period, which is not portable across supported systems.";
                return false;
            }
            if (newBaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || newBaseName.IndexOfAny(PortableInvalidFileNameChars) >= 0
                || newBaseName.Any(char.IsControl))
            {
                cause = "The filename contains a folder separator or another invalid character.";
                return false;
            }

            int dotIndex = newBaseName.IndexOf('.');
            string deviceName = dotIndex >= 0 ? newBaseName.Substring(0, dotIndex) : newBaseName;
            if (ReservedWindowsNames.Any(name => string.Equals(name, deviceName, StringComparison.OrdinalIgnoreCase)))
            {
                cause = $"'{deviceName}' is a reserved filesystem name.";
                return false;
            }

            return true;
        }

        private static bool TryCreatePlan(Asset persistedRoot, string newStoredRoot, out Plan plan, out string cause)
        {
            plan = null;
            cause = null;
            if (persistedRoot == null || string.IsNullOrWhiteSpace(persistedRoot.Location) || string.IsNullOrWhiteSpace(newStoredRoot))
            {
                cause = "The old or new catalog location is empty.";
                return false;
            }

            try
            {
                List<Asset> allAssets = DBAdapter.DB.Table<Asset>().ToList();
                Dictionary<int, List<Asset>> children = allAssets
                    .Where(candidate => candidate.ParentId > 0)
                    .GroupBy(candidate => candidate.ParentId)
                    .ToDictionary(group => group.Key, group => group.ToList());

                Plan result = new Plan
                {
                    RootId = persistedRoot.Id,
                    OldRoot = persistedRoot.Location,
                    NewRoot = newStoredRoot
                };

                Stack<Asset> pending = new Stack<Asset>();
                pending.Push(persistedRoot);
                HashSet<int> visited = new HashSet<int>();
                while (pending.Count > 0)
                {
                    Asset current = pending.Pop();
                    if (!visited.Add(current.Id))
                    {
                        cause = "The package hierarchy contains a parent cycle and cannot be relocated safely.";
                        return false;
                    }

                    string newLocation;
                    if (current.Id == persistedRoot.Id)
                    {
                        newLocation = newStoredRoot;
                    }
                    else if (!TryRebaseLocation(current.Location, persistedRoot.Location, newStoredRoot, out newLocation))
                    {
                        cause = $"Nested package {current.Id} has a location outside the persisted root and cannot be rebased safely.";
                        return false;
                    }

                    result.Changes.Add(new LocationChange
                    {
                        Id = current.Id,
                        OldLocation = current.Location,
                        NewLocation = newLocation
                    });

                    if (!children.TryGetValue(current.Id, out List<Asset> directChildren)) continue;
                    for (int i = 0; i < directChildren.Count; i++) pending.Push(directChildren[i]);
                }

                for (int i = 0; i < result.Changes.Count; i++)
                {
                    for (int j = i + 1; j < result.Changes.Count; j++)
                    {
                        if (!AreLocationsEquivalent(result.Changes[i].NewLocation, result.Changes[j].NewLocation)) continue;
                        cause = $"Catalog entries {result.Changes[i].Id} and {result.Changes[j].Id} would share the same destination.";
                        return false;
                    }
                }

                HashSet<int> movingIds = new HashSet<int>(result.Changes.Select(change => change.Id));
                foreach (Asset other in allAssets)
                {
                    if (movingIds.Contains(other.Id) || string.IsNullOrWhiteSpace(other.Location)) continue;
                    foreach (LocationChange change in result.Changes)
                    {
                        if (!AreLocationsEquivalent(other.Location, change.NewLocation)) continue;
                        string owner = !string.IsNullOrWhiteSpace(other.DisplayName) ? other.DisplayName : other.SafeName;
                        cause = $"The destination is already owned by catalog entry '{owner}' ({other.Id}).";
                        return false;
                    }
                }

                plan = result;
                return true;
            }
            catch (Exception exception)
            {
                cause = $"The relocation plan could not be prepared: {exception.Message}";
                return false;
            }
        }

        private static bool TryRebaseLocation(string storedLocation, string oldRoot, string newRoot, out string rebasedLocation)
        {
            if (Paths.TryRebaseUnderRoot(storedLocation, oldRoot, newRoot, out rebasedLocation)) return true;

            string expandedLocation = Paths.DeRel(storedLocation, true);
            string expandedOldRoot = Paths.DeRel(oldRoot, true);
            string expandedNewRoot = Paths.DeRel(newRoot, true);
            if (string.IsNullOrWhiteSpace(expandedLocation)
                || string.IsNullOrWhiteSpace(expandedOldRoot)
                || string.IsNullOrWhiteSpace(expandedNewRoot)
                || !Paths.TryRebaseUnderRoot(expandedLocation, expandedOldRoot, expandedNewRoot, out string expandedResult))
            {
                rebasedLocation = storedLocation;
                return false;
            }

            rebasedLocation = Paths.MakeRelative(expandedResult);
            return true;
        }

        private static bool AreLocationsEquivalent(string left, string right)
        {
            if (Paths.AreEquivalentPaths(left, right)) return true;

            string expandedLeft = Paths.DeRel(left, true);
            string expandedRight = Paths.DeRel(right, true);
            return !string.IsNullOrWhiteSpace(expandedLeft)
                && !string.IsNullOrWhiteSpace(expandedRight)
                && Paths.AreEquivalentPaths(expandedLeft, expandedRight);
        }

        private static bool TryApplyPlan(Plan plan, out string cause)
        {
            cause = null;
            if (plan == null || plan.Changes.Count == 0)
            {
                cause = "No catalog location changes were prepared.";
                return false;
            }

            try
            {
                DBAdapter.DB.RunInTransaction(() =>
                {
                    foreach (LocationChange change in plan.Changes)
                    {
                        int updated = DBAdapter.DB.Execute(
                            "UPDATE Asset SET Location=? WHERE Id=? AND Location=?",
                            change.NewLocation,
                            change.Id,
                            change.OldLocation);
                        if (updated != 1) throw new InvalidOperationException($"Catalog entry {change.Id} changed while the relocation was being saved.");
                    }
                });
                return true;
            }
            catch (Exception exception)
            {
                cause = $"The catalog transaction failed: {exception.Message}";
                return false;
            }
        }

        private static bool TryBuildFileMoves(
            string sourcePath,
            string destinationPath,
            out List<FileMove> moves,
            out string cause,
            out string recovery)
        {
            moves = new List<FileMove>();
            cause = null;
            recovery = null;
            bool useAssetDatabase = AssetUtils.GetAssetDatabasePath(sourcePath, false) != null;
            moves.Add(new FileMove
            {
                Source = sourcePath,
                Destination = destinationPath,
                UseAssetDatabase = useAssetDatabase
            });

            foreach (string suffix in SidecarSuffixes)
            {
                string sidecarSource = sourcePath + suffix;
                if (!File.Exists(sidecarSource)) continue;
                moves.Add(new FileMove
                {
                    Source = sidecarSource,
                    Destination = destinationPath + suffix,
                    UseAssetDatabase = useAssetDatabase
                });
            }

            if (!useAssetDatabase)
            {
                List<FileMove> contentMoves = moves.ToList();
                foreach (FileMove contentMove in contentMoves)
                {
                    string metaSource = contentMove.Source + ".meta";
                    if (!File.Exists(metaSource)) continue;
                    moves.Add(new FileMove
                    {
                        Source = metaSource,
                        Destination = contentMove.Destination + ".meta",
                        UseAssetDatabase = false
                    });
                }
            }

            foreach (FileMove move in moves)
            {
                if (!File.Exists(move.Source))
                {
                    cause = $"A source file disappeared before it could be moved: '{move.Source}'.";
                    recovery = "Restore the missing file or use Set Location to reconnect the package.";
                    return false;
                }

                if ((File.Exists(move.Destination) || Directory.Exists(move.Destination))
                    && !Paths.AreEquivalentPaths(move.Source, move.Destination)
                    && !IsExistingCaseInsensitiveAlias(move.Source, move.Destination))
                {
                    cause = $"The destination already exists: '{move.Destination}'.";
                    recovery = "Choose a different filename. Existing files are never overwritten.";
                    return false;
                }

                if (!move.UseAssetDatabase) continue;
                string sourceMeta = move.Source + ".meta";
                string destinationMeta = move.Destination + ".meta";
                if (File.Exists(destinationMeta)
                    && !Paths.AreEquivalentPaths(sourceMeta, destinationMeta)
                    && !IsExistingCaseInsensitiveAlias(sourceMeta, destinationMeta))
                {
                    cause = $"The destination metadata file already exists: '{destinationMeta}'.";
                    recovery = "Choose a different filename or remove the orphaned metadata file after verifying it is safe.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryExecuteFileMove(FileMove move, List<MoveLeg> completedLegs, out string cause)
        {
            cause = null;
            bool caseOnly = IsCaseOnlyPathChange(move.Source, move.Destination);
            if (!caseOnly)
            {
                MoveLeg directLeg = new MoveLeg
                {
                    Source = move.Source,
                    Destination = move.Destination,
                    UseAssetDatabase = move.UseAssetDatabase
                };
                if (!TryExecuteMoveLeg(directLeg, out cause)) return false;
                completedLegs.Add(directLeg);
                return true;
            }

            string temporaryPath = CreateTemporarySiblingPath(move.Source);
            MoveLeg temporaryLeg = new MoveLeg
            {
                Source = move.Source,
                Destination = temporaryPath,
                UseAssetDatabase = move.UseAssetDatabase
            };
            if (!TryExecuteMoveLeg(temporaryLeg, out cause)) return false;
            completedLegs.Add(temporaryLeg);

            MoveLeg finalLeg = new MoveLeg
            {
                Source = temporaryPath,
                Destination = move.Destination,
                UseAssetDatabase = move.UseAssetDatabase
            };
            if (!TryExecuteMoveLeg(finalLeg, out cause)) return false;
            completedLegs.Add(finalLeg);
            return true;
        }

        private static bool IsExistingCaseInsensitiveAlias(string sourcePath, string destinationPath)
        {
            if (!IsCaseOnlyPathChange(sourcePath, destinationPath)) return false;

            string directory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;

            try
            {
                string sourceName = Path.GetFileName(sourcePath);
                string destinationName = Path.GetFileName(destinationPath);
                bool foundSource = false;
                bool foundDestination = false;
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    string entryName = Path.GetFileName(entry);
                    if (string.Equals(entryName, sourceName, StringComparison.Ordinal)) foundSource = true;
                    if (string.Equals(entryName, destinationName, StringComparison.Ordinal)) foundDestination = true;
                }

                return !foundSource || !foundDestination;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsCaseOnlyPathChange(string sourcePath, string destinationPath)
        {
            string normalizedSource = Paths.NormalizePathForComparison(sourcePath);
            string normalizedDestination = Paths.NormalizePathForComparison(destinationPath);
            return !string.IsNullOrWhiteSpace(normalizedSource)
                && !string.IsNullOrWhiteSpace(normalizedDestination)
                && string.Equals(normalizedSource, normalizedDestination, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedSource, normalizedDestination, StringComparison.Ordinal);
        }

        private static string CreateTemporarySiblingPath(string sourcePath)
        {
            string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string extension = Path.GetExtension(sourcePath);
            string candidate;
            do
            {
                candidate = Path.Combine(directory, "AssetInventoryRename-" + Guid.NewGuid().ToString("N") + extension).Replace("\\", "/");
            } while (File.Exists(candidate) || Directory.Exists(candidate) || File.Exists(candidate + ".meta"));

            return candidate;
        }

        private static bool TryExecuteMoveLeg(MoveLeg leg, out string cause)
        {
            cause = null;
            try
            {
                if (leg.UseAssetDatabase)
                {
                    string sourceAssetPath = AssetUtils.GetAssetDatabasePath(leg.Source, false);
                    string destinationAssetPath = AssetUtils.GetAssetDatabasePath(leg.Destination, false);
                    if (string.IsNullOrWhiteSpace(sourceAssetPath) || string.IsNullOrWhiteSpace(destinationAssetPath))
                    {
                        cause = $"Unity could not resolve the project asset move from '{leg.Source}' to '{leg.Destination}'.";
                        return false;
                    }

                    string moveError = AssetDatabase.MoveAsset(sourceAssetPath, destinationAssetPath);
                    if (!string.IsNullOrWhiteSpace(moveError))
                    {
                        cause = $"Unity could not move '{sourceAssetPath}' to '{destinationAssetPath}': {moveError}";
                        return false;
                    }
                }
                else
                {
                    File.Move(leg.Source, leg.Destination);
                }

                return true;
            }
            catch (Exception exception)
            {
                cause = string.Format("Could not move '{0}' to '{1}': {2}", leg.Source, leg.Destination, exception.Message);
                return false;
            }
        }

        private static bool TryRollbackMoves(List<MoveLeg> completedLegs, out string cause)
        {
            List<string> failures = new List<string>();
            for (int i = completedLegs.Count - 1; i >= 0; i--)
            {
                MoveLeg completed = completedLegs[i];
                MoveLeg reverse = new MoveLeg
                {
                    Source = completed.Destination,
                    Destination = completed.Source,
                    UseAssetDatabase = completed.UseAssetDatabase
                };
                if (!TryExecuteMoveLeg(reverse, out string reverseCause)) failures.Add(reverseCause);
            }

            cause = failures.Count == 0 ? null : string.Join(" ", failures);
            return failures.Count == 0;
        }

        private static bool TryDetectMultiPartArchive(string sourcePath, out bool multiPart, out string cause)
        {
            multiPart = false;
            cause = null;
            try
            {
                string fileName = Path.GetFileName(sourcePath);
                string lowerName = fileName.ToLowerInvariant();
                if (Regex.IsMatch(lowerName, @"\.part\d+\.rar$")
                    || Regex.IsMatch(lowerName, @"\.(7z|zip)\.\d{3}$")
                    || Regex.IsMatch(lowerName, @"\.r\d{2}$")
                    || Regex.IsMatch(lowerName, @"\.z\d{2}$"))
                {
                    multiPart = true;
                    return true;
                }

                string directory = Path.GetDirectoryName(sourcePath);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return true;

                HashSet<string> siblingNames = new HashSet<string>(
                    Directory.EnumerateFiles(directory).Select(Path.GetFileName),
                    StringComparer.OrdinalIgnoreCase);
                string extension = Path.GetExtension(fileName);
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                if (string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase))
                {
                    multiPart = siblingNames.Contains(baseName + ".r00");
                }
                else if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    multiPart = siblingNames.Contains(baseName + ".z01") || siblingNames.Contains(fileName + ".001");
                }
                else if (string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
                {
                    multiPart = siblingNames.Contains(fileName + ".001");
                }

                return true;
            }
            catch (Exception exception)
            {
                cause = $"Neighboring archive volumes could not be inspected: {exception.Message}";
                return false;
            }
        }

        private static bool RequiresFolderLocation(Asset asset)
        {
            return asset.AssetSource == Asset.Source.Directory
                || asset.AssetSource == Asset.Source.RegistryPackage
                && (asset.PackageSource == UnityEditor.PackageManager.PackageSource.Embedded
                    || asset.PackageSource == UnityEditor.PackageManager.PackageSource.Local);
        }

        private static string FormatFailure(string problem, string cause, string recovery)
        {
            return $"{problem}\n\nCause: {cause}\n\nRecovery: {recovery}";
        }
    }
}
