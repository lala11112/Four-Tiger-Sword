---
name: "asset-inventory-setup-and-overview"
description: "Use this skill whenever the user asks to 'open Asset Inventory', set up its first index, understand its Search and Packages workflows, or choose the right Asset Inventory skill. Use asset-inventory-find-assets-across-sources for a concrete search request."
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

# Asset Inventory Setup and Overview

Open Asset Inventory, confirm its catalog state and configured sources, and route the request without assuming where the package is installed.

## When to use this skill

- Opening the main Asset Inventory window.
- Choosing an initial indexing path and checking readiness.
- Routing searches, organization, imports, audits, exports, and automation.

## Prerequisites

- Asset Inventory is imported into a Unity 2022.3 or newer project.
- A live Unity Editor is available; batch and headless catalog behavior is not a supported customer contract.

## Quick start

1. Open `Tools > Asset Inventory > Asset Inventory`.
2. Alternatively use `Window > Package Management > Asset Inventory` on Unity 6.1+, or `Window > Asset Inventory` on earlier supported Editors.
3. If the catalog is empty, choose automatic or choose-first indexing and let the initial actions complete.
4. Select the narrowest sibling skill for the requested outcome.

## Workflows

### Workflow: Open and assess readiness

1. Open the main window.
2. Check whether indexing is complete or resumable.
3. Review configured Asset Store, package, project, and additional-folder sources.
4. Resolve any visible not-ready state before searching or mutating.

### Workflow: Choose a workflow

1. Use Search for indexed files and media.
2. Use Packages for package-level discovery, versions, and maintenance.
3. Use Reporting, dependency, validator, action, or export surfaces only when their focused job is requested.

## Verification

- The main window opens without Console errors.
- Catalog statistics or a clear setup state are visible.
- The intended sources are enabled and their paths remain valid.
- No indexing or mutation was started outside the user's stated scope.

## API quick reference

- Main menu: `Tools > Asset Inventory > Asset Inventory`.
- Alternative menu: `Window > Package Management > Asset Inventory` on Unity 6.1+; `Window > Asset Inventory` on Unity 2022.3 through Unity 6.0.
- Project context menu: `Assets > Asset Inventory`.
- Welcome guide: `Tools > Asset Inventory > Welcome`; main-toolbar buttons are available on Unity 6.3+.
- Automation queries initialize the catalog on demand but UI commands still require a live Editor.

## Common issues

- **The catalog is not ready:** Complete or resume the visible indexing setup before relying on results.
- **A source path moved:** Use Asset Inventory's source or additional-folder relocation workflow rather than editing database paths.
- **Two Editors are open:** Target the intended project explicitly for any CLI or MCP operation.

## Boundaries

- Do not use or expose experimental semantic or code-search surfaces.
- Do not assume the package lives under `Assets/_Tools` or any fixed import path.
- Do not alter database schema or upgrader versions for ordinary use.
- Use preview-bound confirmation for automation changes to catalog data, files, downloads, actions, or scene objects. Opening and closing windows are direct UI lifecycle operations without a dry-run token.
