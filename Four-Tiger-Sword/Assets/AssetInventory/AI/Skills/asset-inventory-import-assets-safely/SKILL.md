---
name: "asset-inventory-import-assets-safely"
description: "Use this skill whenever the user asks to 'import this asset', bring a package file into the project, include dependencies, place an imported prefab in the scene, or download an Asset Store package through Asset Inventory. Use asset-inventory-find-assets-across-sources when the target is not yet verified."
metadata:
  asset: "Asset Inventory 4"
  publisher: "Impossible Robert"
  asset-version: "4.9.0"
  skill-version: "1.0.0"
  unity: "2022.3"
  render-pipelines: "Not applicable"
  category: "Tools/Utilities"
  asset-store-url: "https://assetstore.unity.com/packages/tools/utilities/asset-inventory-4-349582"
  documentation-url: "https://www.wetzold.com/tools/assetinventory/docs/"
  support-url: "https://discord.com/invite/uzeHzEMM4B"
  last-verified: "2026-10-02"
---

# Import Assets Safely

Inspect an exact catalog item and its dependencies, review the configured collision policy, then preview its identity and destination before a confirmed import or scene placement.

## When to use this skill

- Importing one indexed file or a selected package subset.
- Downloading and monitoring an Asset Store package.
- Adding an already imported prefab or model to the active scene.

## Prerequisites

- The exact package and file identity are verified.
- Destination, dependency inclusion, overwrite behavior, and active scene are known.

## Quick start

1. Inspect the target's details, preview, source path, and complete dependency state; review collision behavior in Import settings.
2. Choose a project-relative destination under `Assets`.
3. Run the mutation as a dry run and review the returned identity, destination, and options. Import dry-run does not expand a dependency list or simulate collisions.
4. Execute only with the matching confirmation token, then verify imported assets and scene state.

## Workflows

### Workflow: Import an indexed file

1. Resolve one exact indexed file.
2. Inspect dependencies and package-relative paths in the UI, then review the target folder and `AI.Config.importCollisionMode` separately.
3. Run the default dry run.
4. Review the dry-run file ID, package ID, target folder, dependency option, and script mode. Repeat the unchanged request with execution enabled and the returned token.
5. Verify imported files, GUID handling, and Console state.

### Workflow: Place an imported asset in the scene

1. Confirm the prefab or model is already under the project's `Assets` directory.
2. Resolve one active loaded scene and an unambiguous parent.
3. Preview the scene placement and coordinates.
4. Execute with confirmation, then verify Undo and scene dirty state.

## Verification

- Imported paths remain inside the project `Assets` directory.
- Imported files match the inspected dependency scope and configured collision behavior; the dry-run response alone cannot prove either.
- Existing GUID and collision behavior is reported.
- Scene placement targets one active scene and is undoable.

## API quick reference

- Import parameters include `FileId`, `TargetFolder`, `WithDependencies` (default true), and `ScriptMode` (0 none, 2 direct, 3 extended, 4 all package scripts).
- Mutations default to `DryRun = true`.
- Execution requires the current SHA-256 confirmation token and unchanged validated scope.
- Download progress is available through a separate read-only query.
- A file already present in the project returns `alreadyInProject = true` and its path without copying or requiring a new token. Inspect that path before continuing to scene placement.
- The token binds the import identity and request options; it does not snapshot the dependency graph, collision policy, or project filesystem. Recheck those before execution when they changed.

## Common issues

- **Confirmation mismatch:** The scope changed after preview; accept the replacement preview and use its new token.
- **Destination is rejected:** Choose a normalized project path under `Assets` without traversal.
- **Scene parent is ambiguous:** Use a unique parent or omit it and provide deterministic coordinates.

## Boundaries

- Never bypass dry-run confirmation.
- Do not import from an unverified name-only match.
- Do not overwrite existing user assets unless the preview makes that scope explicit.
- Do not place objects into an ambiguous or unloaded scene.
