# Codex Prompt: Modern WPF Reimplementation of EVE IPH

Use the following prompt in a fresh Codex session when starting the new application.

---

You are Codex acting as a senior .NET engineer. Build a new modern C# WPF application inspired by the existing legacy VB.NET WinForms application **EVE Isk per Hour**. The goal is **not** to translate the old application 1:1. The goal is to extract the business behavior, workflows, calculations, and data model assumptions from the legacy codebase and reimplement them in a clean, testable, modern WPF architecture.

## Source Context

The legacy repository is a VB.NET Windows Forms application targeting .NET Framework 4.6.1. Important source files:

- `frmMain.vb`: main UI shell and workflow coordinator. Large file mixing UI, SQL, state, and calculation orchestration.
- `Globals.vb`: global constants, shared mutable state, utility helpers, DB helpers, update logic entry points.
- `Blueprint.vb`: core blueprint/manufacturing calculation model.
- `ManufacturingFacility.vb`: facility selection, structure/facility bonuses, cost indexes, taxes, refining settings.
- `Materials.vb` and `Material.vb`: material collection/value models.
- `ShoppingList.vb`: shopping list aggregation and build/buy calculations.
- `MarketPriceInterface.vb`: market order/history update coordination.
- `ESI.vb`: EVE SSO, ESI client, token storage, ESI DTOs, public/private endpoint calls.
- `EVEAssets.vb`, `EVEBlueprints.vb`, `EVEIndustryJobs.vb`, `EVESkillList.vb`, `Character.vb`, `Corporation.vb`: character, corporation, skills, assets, blueprints, and jobs import/cache logic.
- `ProgramSettings.vb`: XML-based settings for tabs and user preferences.
- `DBConnection.vb`: SQLite connection wrapper.
- `EVE-IPH-Update-Program/frmUpdaterMain.vb`: old updater and DB migration tool.

There is also a repository map in `docs/program-map.md`. Read it first.

## Target

Create a new solution, tentatively named `EveIndustryPlanner`, using modern C# and WPF. Prefer .NET 8 or newer if available locally. If the environment only supports another current .NET SDK, use that and document the decision.

The new application should preserve the important functional intent:

- browse/search EVE blueprints;
- calculate manufacturing inputs and outputs;
- support ME/TE, runs, blueprint type, invention/copy/reaction-related inputs where relevant;
- calculate raw material and component material requirements;
- support facility/cost/tax modifiers;
- load or model market prices;
- produce profitability metrics such as total cost, revenue, profit, margin, and ISK/hour;
- produce a shopping list from required materials;
- provide a clean path for future ESI integration.

Do **not** copy the old UI layout 1:1. Build a modern WPF experience with clear workflows, testable domain code, and minimal global state.

## Core Principles

1. Treat the legacy app as a source of behavior, not as a code template.
2. Keep WPF views thin. Business logic belongs in domain/services/projects, not code-behind.
3. Add automated tests for extracted rules before broadening functionality.
4. Prefer explicit models and interfaces over shared globals.
5. Use dependency injection.
6. Use async APIs for IO/network/database work.
7. Avoid direct SQL scattered through UI code.
8. Make the first milestone small and working before expanding.

## Proposed Solution Structure

Create a solution with these projects:

```text
EveIndustryPlanner.sln
src/
  EveIndustryPlanner.App/          WPF application
  EveIndustryPlanner.Core/         domain models and calculation engine
  EveIndustryPlanner.Data/         SQLite/SDE data access
  EveIndustryPlanner.Esi/          ESI/SSO client abstractions and future implementation
  EveIndustryPlanner.Infrastructure/ shared services: settings, logging, clocks, caching
tests/
  EveIndustryPlanner.Core.Tests/
  EveIndustryPlanner.Data.Tests/
```

If this structure is too much for the first commit, create at least:

```text
src/EveIndustryPlanner.App
src/EveIndustryPlanner.Core
tests/EveIndustryPlanner.Core.Tests
```

But design the namespaces so the missing projects can be added cleanly.

## First Milestone: Manufacturing Calculator MVP

Implement the first usable workflow:

1. User searches/selects a blueprint.
2. User enters:
   - runs;
   - ME;
   - TE;
   - blueprint copy/original state if needed;
   - facility profile;
   - price profile.
3. App calculates:
   - required materials;
   - total material cost;
   - manufacturing job cost/fees where data is available;
   - estimated output revenue;
   - profit;
   - profit percent;
   - ISK per hour if production time data is available.
4. App displays:
   - calculation summary;
   - material list;
   - shopping list export.

Start with deterministic local/sample data if a complete EVE SDE database is not yet wired. Do not block the app scaffold on ESI OAuth or updater migration.

## Legacy Behavior To Extract First

Study these legacy areas in order:

1. `frmMain.RunBlueprint`
   - Identify the inputs collected from the UI.
   - Identify how `Blueprint` is constructed.
   - Identify which facilities and settings are passed into the calculation.
   - Identify which labels/lists are populated from the result.

2. `Blueprint.vb`
   - Constructor around blueprint ID/runs/ME/TE.
   - `BuildItems`.
   - raw materials vs component materials;
   - invention/copy cost hooks;
   - `SetPriceData`;
   - production time calculations;
   - getters used by `frmMain`.

3. `ManufacturingFacility.vb`
   - `IndustryFacility` model;
   - production type enums;
   - modifiers for material, time, usage, taxes, and facility bonuses;
   - manual overrides.

4. `Materials.vb` / `Material.vb`
   - material identity;
   - quantities;
   - total cost/volume;
   - list aggregation;
   - export text formats.

5. `ShoppingList.vb`
   - aggregation of material requirements;
   - build/buy handling;
   - price updates.

Document each extracted rule in comments or markdown before reimplementing complex behavior.

## Domain Model Sketch

Create clean C# models roughly like:

```csharp
public sealed record BlueprintId(long Value);
public sealed record TypeId(long Value);

public sealed class BlueprintDefinition
{
    public BlueprintId BlueprintId { get; init; }
    public TypeId ProductTypeId { get; init; }
    public string BlueprintName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public int TechLevel { get; init; }
    public int ProductQuantity { get; init; }
    public TimeSpan BaseProductionTime { get; init; }
    public IReadOnlyList<BlueprintMaterial> Materials { get; init; } = [];
}

public sealed class BlueprintMaterial
{
    public TypeId TypeId { get; init; }
    public string Name { get; init; } = "";
    public long Quantity { get; init; }
    public MaterialCategory Category { get; init; }
}

public sealed class ManufacturingRequest
{
    public BlueprintId BlueprintId { get; init; }
    public int Runs { get; init; }
    public int MaterialEfficiency { get; init; }
    public int TimeEfficiency { get; init; }
    public FacilityProfile Facility { get; init; } = FacilityProfile.None;
    public PriceProfile PriceProfile { get; init; } = PriceProfile.Empty;
}

public sealed class ManufacturingResult
{
    public IReadOnlyList<MaterialRequirement> Materials { get; init; } = [];
    public decimal MaterialCost { get; init; }
    public decimal JobCost { get; init; }
    public decimal TotalCost { get; init; }
    public decimal EstimatedRevenue { get; init; }
    public decimal Profit { get; init; }
    public decimal ProfitPercent { get; init; }
    public decimal IskPerHour { get; init; }
    public TimeSpan TotalProductionTime { get; init; }
}
```

Adjust names as the implementation evolves. Keep models immutable where practical.

## Services And Interfaces

Create interfaces before concrete infrastructure:

```csharp
public interface IBlueprintRepository
{
    Task<IReadOnlyList<BlueprintSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<BlueprintDefinition?> GetBlueprintAsync(BlueprintId blueprintId, CancellationToken cancellationToken);
}

public interface IMarketPriceProvider
{
    Task<MarketPrice?> GetPriceAsync(TypeId typeId, PriceProfile profile, CancellationToken cancellationToken);
}

public interface IManufacturingCalculator
{
    Task<ManufacturingResult> CalculateAsync(ManufacturingRequest request, CancellationToken cancellationToken);
}

public interface IShoppingListService
{
    ShoppingList CreateFromManufacturingResult(ManufacturingResult result);
}
```

Keep the first implementation simple. For example, use in-memory sample repositories first, then SQLite repositories.

## WPF UI Requirements

Use MVVM. Do not put calculation logic in code-behind.

Recommended views:

- `MainWindow`
  - left navigation or tabs;
  - first view: Manufacturing Calculator.
- `ManufacturingCalculatorView`
  - blueprint search box;
  - selected blueprint details;
  - runs/ME/TE inputs;
  - facility selector;
  - price profile selector;
  - calculate button;
  - summary panel;
  - material requirements grid;
  - shopping list/export area.

Use WPF binding and commands:

- `ManufacturingCalculatorViewModel`;
- `AsyncRelayCommand` or equivalent;
- observable state for loading/error/result;
- validation for runs, ME, TE.

Do not over-style early. Use a clean professional layout, readable grids, and responsive sizing. Focus on working behavior first.

## Data Strategy

The legacy app uses SQLite extensively. For the new app:

1. Define repository interfaces in Core.
2. Put SQLite implementation in Data.
3. Do not let WPF view models execute raw SQL.
4. Use parameterized SQL.
5. Add migrations or explicit schema checks.

For the first milestone, acceptable options:

- use a small embedded/sample JSON dataset for a few blueprints;
- or read from an existing SQLite SDE database if available;
- or create a minimal local SQLite schema just for MVP.

Document which option was chosen.

Minimum sample data should include:

- at least one T1 blueprint;
- several required materials;
- a product item;
- market prices for materials and output item;
- base production time.

## ESI Strategy

Do not implement full ESI OAuth in the first milestone unless explicitly requested. Create the `EveIndustryPlanner.Esi` project and interfaces only if useful.

Future ESI modules should cover:

- SSO with PKCE;
- token refresh;
- character identity;
- character skills;
- assets;
- blueprints;
- industry jobs;
- market orders/history;
- public structures and structure markets.

When implementing ESI later, use typed DTOs, cancellation tokens, rate-limit handling, cache dates, and structured error results. Avoid the legacy pattern of broad global error state.

## Calculation Rules To Pay Attention To

Extract these from the old code before implementing broad behavior:

- material efficiency rounding behavior;
- how runs multiply materials;
- how product quantity/portion size affects output count;
- raw material vs component build mode;
- build/buy decisions;
- taxes and broker fees;
- facility material/time/cost modifiers;
- system cost indexes;
- invention/copy costs;
- excess material handling;
- reprocessing/ore conversion hooks;
- price source precedence;
- missing price behavior.

When unsure, write a test that captures the old observed behavior for one known blueprint.

## Testing Requirements

Set up unit tests from the beginning.

Initial tests:

1. Calculator returns expected material quantities for a simple blueprint.
2. ME reduces material quantities according to the implemented rounding rule.
3. Runs multiply output and material requirements correctly.
4. Missing price produces a clear warning/result state instead of an exception.
5. Shopping list aggregates duplicate materials.

Prefer xUnit or NUnit. Use FluentAssertions if available and acceptable.

Tests should not require live ESI or internet.

## Migration Approach

Work incrementally:

1. Create solution and project structure.
2. Create Core models and interfaces.
3. Add sample data repository.
4. Implement manufacturing calculator MVP.
5. Add unit tests.
6. Build WPF manufacturing screen.
7. Add shopping list export.
8. Replace sample data with SQLite/SDE data access.
9. Add price provider abstraction and real providers.
10. Add ESI integration later.

Do not attempt a full rewrite in one pass.

## Acceptance Criteria For First Milestone

The first milestone is complete when:

- solution builds from command line;
- tests pass;
- WPF app opens;
- user can select/search at least one sample blueprint;
- user can enter runs/ME/TE;
- calculation returns material requirements;
- total cost/revenue/profit are displayed;
- material list can be copied/exported as text;
- code is organized so business logic is not in WPF code-behind.

## Build Commands

After creating the solution, document and verify commands such as:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/EveIndustryPlanner.App
```

If WPF requires Windows-only targeting, configure the project accordingly, for example:

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
```

## Important Non-Goals For The First Milestone

Do not implement:

- full old UI parity;
- updater;
- complete ESI SSO;
- all tabs from the legacy app;
- every blueprint category;
- corporation workflows;
- public structures;
- full settings migration;
- exact old XML settings format;
- automatic update prompts.

These can be future milestones.

## Working Style

Before coding each major feature:

1. Inspect the relevant legacy files.
2. Summarize the old behavior.
3. Decide what belongs in Core vs Data vs App.
4. Write or update tests.
5. Implement the smallest working slice.
6. Run build/tests.
7. Document any intentional divergence from legacy behavior.

Keep changes focused. Prefer a working vertical slice over broad incomplete scaffolding.

## First Task

Start by creating the solution skeleton and the Manufacturing Calculator MVP using sample data. Then write a short `docs/architecture.md` explaining:

- project structure;
- current implemented workflow;
- sample data source;
- calculation assumptions;
- next recommended milestones.

Do not integrate live ESI yet. Do not depend on the old updater. Do not mutate the legacy repository unless explicitly instructed.
