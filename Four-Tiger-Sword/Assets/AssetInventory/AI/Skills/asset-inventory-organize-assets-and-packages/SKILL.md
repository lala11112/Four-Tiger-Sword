---
name: "asset-inventory-organize-assets-and-packages"
description: "Use this skill whenever the user asks to 'tag these packages', group assets, create a reusable filter, save a search, or maintain an organized Asset Inventory catalog. Use asset-inventory-find-assets-across-sources when the user only needs a one-time search."
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

# Organize Assets and Packages

Organize verified catalog items with tags, groups, and saved searches while preserving stable package identity and existing user organization.

## When to use this skill

- Tagging packages or indexed files.
- Creating, applying, and maintaining file or package saved searches.
- Using package groups and indexing-maintenance views.

## Prerequisites

- The exact target packages or indexed files are identified.
- Tag names, hierarchy, saved-search filters, and overwrite behavior are explicit.

## Quick start

1. Start from a verified Search or Packages result set.
2. Reuse an existing tag or group when its meaning already matches.
3. Create or update the smallest saved search that captures the intended filters.
4. Apply it again and confirm persistence and active-state behavior.

## Workflows

### Workflow: Tag selected catalog items

1. Inspect existing tags and hierarchy.
2. Preview the exact package or file targets.
3. Apply tags only after confirming scope.
4. Re-run the matching tag filter to verify membership.

### Workflow: Create a saved search

1. Configure the complete Search or Packages state.
2. Save it with a clear name and optional color.
3. Change filters, then reapply the saved search.
4. Verify rename, reorder, highlighting, and restart persistence when relevant.

## Verification

- Only intended packages or files received tags.
- Existing tag hierarchy remains intact.
- The saved search restores every persisted filter in its model.
- Manual filter changes clear active saved-search state when no longer matching.

## API quick reference

- Automation queries and mutations include listing tags, tagging a package, and tagging an indexed file.
- `SavedSearch` stores file-search state.
- `SavedPackageSearch` stores package and maintenance state.
- Seeded package searches include All, Recently Purchased, Recently Updated, Update Available, and Asset Store Only.

## Common issues

- **A saved package search does not reactivate:** Check every persisted header, maintenance, grouping, and inspector filter.
- **A tag applies to the wrong item:** Resolve the stable package or file identity before mutation.
- **Indexing filters were implemented separately:** Use package maintenance conditions such as Not Included in Indexing, Indexing Enabled, or Needs Indexing.

## Boundaries

- Do not delete or reparent existing tags without explicit confirmation.
- Do not add a separate indexing quick-view model.
- Do not assume name-only package matching is safe.
- Every automation tag mutation must use dry-run and confirmation token execution.
