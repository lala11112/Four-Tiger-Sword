# Asset Inventory AI Skills

These skills teach a coding assistant to use Asset Inventory's indexed catalog, organization, import, analysis, export, and guarded automation workflows.

## Use the skills

1. Point a compatible coding assistant at this package's `AI/Skills` folder.
2. Ask it to read the skill descriptions and load `asset-inventory-setup-and-overview` first when setup or routing is unclear.
3. Load only the narrowest skill that matches the requested job.
4. Review every proposed project change before allowing the assistant to apply it.

These Markdown files are documentation only. They do not add an AI service, subscription, runtime dependency, or generated content to Asset Inventory.

## Included skills

- `asset-inventory-setup-and-overview`: Open Asset Inventory, confirm its catalog state and configured sources, and route the request without assuming where the package is installed.
- `asset-inventory-find-assets-across-sources`: Search Asset Inventory's indexed files and packages with bounded filters, verify source identity, and return actionable results rather than broad guesses.
- `asset-inventory-organize-assets-and-packages`: Organize verified catalog items with tags, groups, and saved searches while preserving stable package identity and existing user organization.
- `asset-inventory-import-assets-safely`: Identify the exact catalog item, inspect dependencies and destination collisions, then obtain the operation preview and confirmation before importing or placing it.
- `asset-inventory-audit-dependencies-and-duplicates`: Run the narrowest dependency, usage, duplicate, or maintenance audit, inspect findings, and apply only supported fixes with explicit scope.
- `asset-inventory-automate-catalog-workflows`: Invoke Asset Inventory's transport-neutral automation through an installed adapter, target the intended Unity project explicitly, and preserve dry-run confirmation for mutations.

## Current workflow contracts

The bundled automation adapters activate when a supported companion is installed: Unity AI Assistant from `2.0.0-pre` onward, or Unity Pipeline in `[0.8.0-exp.1, 0.9.0-0)`. Their command groups delegate to the shared catalog implementation. Confirm the intended project before issuing commands; an adapter response does not guarantee that a project path is included in every result.

Mutations use a dry-run preview and a matching confirmation token. The import preview identifies the requested file, package, target folder, dependency option, and script mode; it does not enumerate the dependency closure or simulate destination collisions. Action-run previews list steps and their enabled state without simulating their effects. Inspect those details separately before approval. Opening and closing the main window are immediate UI operations with no dry-run parameters.

Maintenance checks operate on their catalog or storage scope. A check-name filter changes which checks are visible; it does not narrow the records they inspect. A supported repair applies to the check's complete findings collection, so review that full collection before requesting repair.

Each skill's `last-verified` date records a static source and workflow review. It does not certify a successful live Unity journey. Repository benchmark scenarios retain `NOT RUN` until executed with their stated fixture and evidence. Read the current package manifest and source version rather than treating the skill's `asset-version` as a release decision.

[Documentation](https://www.wetzold.com/tools/assetinventory/docs/) | [Support](https://discord.com/invite/uzeHzEMM4B)
