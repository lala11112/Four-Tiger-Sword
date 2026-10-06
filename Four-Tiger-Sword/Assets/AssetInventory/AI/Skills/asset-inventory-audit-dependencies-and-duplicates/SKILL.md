---
name: "asset-inventory-audit-dependencies-and-duplicates"
description: "Use this skill whenever the user asks to 'find unused assets', inspect dependencies, detect duplicate packages or media, validate previews, or repair Asset Inventory catalog integrity. Use asset-inventory-import-assets-safely when the primary goal is bringing a verified item into the project."
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

# Audit Dependencies and Duplicates

Run the narrowest dependency, usage, duplicate, or maintenance audit, inspect findings, and apply only supported fixes with explicit scope.

## When to use this skill

- Inspecting package or asset dependency graphs and project usage.
- Finding duplicate package entries or duplicate indexed media.
- Running database, preview, media, cache, and backup validators.

## Prerequisites

- Relevant packages and project assets are indexed.
- The audit target and whether fixes are allowed are explicit.

## Quick start

1. Choose dependency or usage analysis for relationships, or Maintenance validators for integrity.
2. Run the narrowest applicable analysis and wait for completion.
3. Inspect every finding and distinguish source files from generated cache files. Maintenance checks generally scan the catalog or configured storage, not just the selected package.
4. Review the complete issue list for a fixable check before invoking its repair, then rerun that check.

## Workflows

### Workflow: Inspect dependencies and usage

1. Select the package or project scope.
2. Build the dependency or usage view.
3. Search and inspect graph and list details.
4. Report direct, transitive, missing, or unused relationships without deleting anything.

### Workflow: Run a maintenance validator

1. Open the Maintenance surface and filter check names to find the relevant validator; this changes the visible check list, not its scan scope.
2. Run it and inspect database or filesystem findings.
3. Use Fix only when the validator advertises support and its complete current issue collection is approved. Show opens an issue list, not an item-selection repair dialog.
4. Rerun and confirm deterministic completion.

## Verification

- Analysis scope matches the request.
- Findings distinguish database rows, source files, previews, and caches.
- No non-fixable issue is presented as automatically repaired.
- A post-fix run no longer reports approved resolved items.
- A request to repair only a subset cannot use a validator whose `Fix()` processes the whole collection; report that limit before changing data.

## API quick reference

- The current validator catalog contains database/index, preview/media, cache, and backup checks.
- `DuplicatePackageEntriesValidator` reconciles into a canonical row without deleting package source files.
- Generated preview cleanup must use cache-owned preview deletion paths.
- Dependency and usage views are read-only until a separate explicit action is invoked.

## Common issues

- **A duplicate name is not a duplicate identity:** Compare stable package records, source paths, foreign IDs, and versions.
- **A preview fix targets an original file:** Stop; generated-preview cleanup must never delete a `UseOriginal` source.
- **A validator cannot fix its result:** Report the issue and required manual action instead of inventing a repair.

## Boundaries

- Do not delete source package files during duplicate reconciliation.
- Do not infer unused status from one incomplete index.
- Do not extend or expose experimental search systems.
- Respect validator cancellation and supported fixability.
- Avoid Fix All when only one reviewed check is authorized.
