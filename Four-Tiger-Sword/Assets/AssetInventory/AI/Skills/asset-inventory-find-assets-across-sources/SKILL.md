---
name: "asset-inventory-find-assets-across-sources"
description: "Use this skill whenever the user asks to 'find an asset', search purchased packages, locate project files, filter media across folders, or compare indexed package results in Asset Inventory. Use asset-inventory-organize-assets-and-packages when the primary goal is tagging or saving the result set."
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

# Find Assets Across Sources

Search Asset Inventory's indexed files and packages with bounded filters, verify source identity, and return actionable results rather than broad guesses.

## When to use this skill

- Finding indexed files across Asset Store packages, installed packages, the project, and additional folders.
- Searching or filtering packages by name, metadata, state, or source.
- Inspecting package files and details before an import.

## Prerequisites

- Relevant sources have completed indexing.
- The request identifies a useful query, asset type, package, or filter.

## Quick start

1. Choose Search for file-level results or Packages for package-level results.
2. Apply the narrowest text, type, source, tag, size, date, or maintenance filters.
3. Keep result pages bounded and inspect likely matches.
4. Verify package and source identity before using a result.

## Workflows

### Workflow: Find a file or media asset

1. Enter a focused query in Search.
2. Apply file type and source filters before widening terms.
3. Inspect preview, package, path, and metadata for each likely match.
4. Return the best matches with their owning package and intended action.

### Workflow: Find a package

1. Use Packages and search by title, publisher, category, or known metadata.
2. Apply purchase, update, indexing, source, or tag filters.
3. Open package details and inspect versions and contained files.
4. Distinguish similarly named or duplicate packages by stable identity and location.

## Verification

- Results come from the requested sources.
- Every returned item has a verified package or filesystem identity.
- Pagination and result limits remain bounded.
- No import or tag mutation occurs unless separately requested and confirmed.

## API quick reference

- Automation query: `asset_inventory_search_assets`.
- Automation query: `asset_inventory_search_packages`.
- Automation query: `asset_inventory_search_project_assets`.
- Related queries include package details, package files, inventory statistics, groups, and project GUID checks.
- `search_assets` has no source or package-ID parameter. To restrict automation to a known source, use `search_packages` with its `Source` filter, then `list_package_files` with each verified `PackageId`; the interactive Search source filter remains available.
- Choose either `Type` or `AssetGroup`, not both. File and package automation searches accept pages of 1-100 results; package-file queries accept 1-200.

## Common issues

- **Expected files are missing:** Check source readiness, hidden/excluded filters, and whether the owning package has indexed files. Do Not Index retains existing results; Exclude hides them. Run indexing only when needed and requested.
- **Results are too broad:** Add type, source, package, tag, or range filters rather than relying on longer free text.
- **An expert query is rejected:** Use one leading `=` expression and remove comments, separators, NULs, or unterminated quotes.

## Boundaries

- Searches are read-only and should remain bounded.
- Do not silently broaden unknown or ambiguous filter values.
- Do not treat display names as unique package identity.
- Use the import skill only after the target file and package are verified.
