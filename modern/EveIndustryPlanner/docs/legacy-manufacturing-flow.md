# Legacy Manufacturing Flow

This note captures the first manufacturing path extracted from the VB.NET WinForms application. It is the reference for turning the WPF MVP into a behaviorally compatible planner.

## Entry Point

`frmMain.RunBlueprint` is the main UI entry point for a blueprint calculation. It receives the blueprint id, runs, ME/TE, additional costs, manufacturing facilities, build/buy preferences, reprocessing settings, blueprint tech level, invention/copy facilities, and relic name.

For T2/T3 blueprints with invention enabled, it builds one or more decryptor scenarios. When optimal decryptor mode is active, it evaluates all decryptors plus `NoDecryptor` and selects the result with the best profit or ISK/hour. For T1 or ignored invention, it constructs a single `Blueprint` and calls `BuildItems`.

## Blueprint Construction

`Blueprint.New` loads blueprint/product metadata from SQLite, stores user settings, saves `AdditionalCosts`, assigns the main/component/capital/reaction/reprocessing facilities, initializes invention/copy state, and derives facility flags such as activity usage inclusion.

The constructor does not perform the full calculation. The actual material, time, facility usage, tax, and profit outputs are populated later by `BuildItems`, `BuildItem`, and `SetPriceData`.

## BuildItems

`Blueprint.BuildItems` delegates to `BuildItem` when one blueprint is used. For multiple blueprints it splits runs into batches, calculates each batch, merges raw materials, component materials, built component metadata, job fees, EIV, taxes, and facility usage, then rebuilds component blueprints so shopping list data remains consistent.

After materials are collected, optional reprocessing can convert minerals/ice to ore and recalculates prices.

## Core Formulas Found

Material quantity in `BuildItem` is:

```text
max(runs, ceil(round(runs * baseQuantity * materialModifier, 2)))
materialModifier = (1 - ME / 100) * facilityMaterialMultiplier
```

Production time is based on base production time, TE, facility time multiplier, implant and industry skill modifiers, then adjusted for batches and component build times.

`SetPriceData` computes final totals:

```text
TotalRawCost = raw materials + invention + copy + taxes/fees + additional costs + usage - sold excess
TotalComponentCost = component materials + invention + copy + taxes/fees + additional costs + selected usage - sold excess
```

## MVP Extraction Status

The modern Core currently implements the simple raw-material path with ME, TE, facility multipliers, job cost, revenue, profit, ISK/hour, shopping list output, warnings for missing prices, and additional costs. Remaining legacy behavior includes build/buy recursion, invention/copy costs, decryptor optimization, component facility selection, taxes/broker fees, excess material handling, reprocessing, skills, implants, and real SQLite/SDE data.
