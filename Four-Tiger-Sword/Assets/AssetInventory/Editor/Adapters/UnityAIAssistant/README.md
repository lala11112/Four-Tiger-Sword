# Asset Inventory Unity AI Assistant Integration

This adapter is bundled with Asset Inventory and requires no separate activation step.

When Unity AI Assistant `2.0.0-pre.1` or newer is installed, the adapter assembly compiles automatically and Assistant discovers all 31 established `AssetInventory_*` tools. Every tool declares `EnabledByDefault = true`, so a fresh Assistant profile exposes them immediately. An explicit user disable in Assistant settings remains authoritative.

When Assistant is absent, the package version define excludes this assembly and the base Asset Inventory package remains compilable.

All operations delegate to `AssetInventory.Automation`; this adapter assembly contains no independent business behavior.
