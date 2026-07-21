# Eve Industry Planner Architecture

This is the first modern WPF slice extracted from the legacy EVE IPH codebase. It is not a 1:1 rewrite. The current goal is a clean, testable manufacturing calculator MVP that can later absorb more behavior from the VB.NET application.

## Project Structure

```text
EveIndustryPlanner.sln
src/
  EveIndustryPlanner.App/      WPF UI and view models
  EveIndustryPlanner.Core/     domain models, service interfaces, calculator, sample data
tests/
  EveIndustryPlanner.Core.Tests/
```

`EveIndustryPlanner.Core` has no WPF dependency. It owns the calculation inputs, outputs, sample repositories, and services. `EveIndustryPlanner.App` is a thin WPF/MVVM layer over the Core services.

## Implemented Workflow

The first screen supports a manufacturing calculator flow:

1. Search/select a sample blueprint.
2. Enter runs, ME, TE, and optional additional costs.
3. Calculate material requirements.
4. Display material cost, job cost, total cost, revenue, profit, margin, ISK/hour, and production time.
5. Display required materials in a grid.
6. Generate and copy a simple multibuy-style shopping list.

The app now prefers local SDE YAML data from `src/sde` when `blueprints.yaml` and `types.yaml` are present. If those files are missing, it falls back to the sample Merlin/Rifter data. Market prices are loaded from Fuzzworks aggregates and cached under `%LOCALAPPDATA%\EveIndustryPlanner\fuzzworks-prices.json`; SDE `basePrice` remains a fallback.

User market preferences are persisted under `%LOCALAPPDATA%\EveIndustryPlanner\settings.json`. The file stores material/product market locations and price strategies only.
Facility preferences are stored in the same file. Each saved facility contains a name, solar system name/id, material/time/job multipliers, system cost index, and facility tax. The calculator can use separate facilities for final products, manufacturing components, and reactions.

## Calculation Assumptions

The MVP intentionally uses simple deterministic rules:

- material quantity = `ceil(base quantity * runs * (1 - ME/100) * facility material multiplier)`;
- production time = `base time * runs * (1 - TE/100) * facility time multiplier`;
- job cost = `estimated output value * system cost index * facility job cost multiplier`;
- total cost = material cost + job cost + additional costs;
- material prices use sell price by default;
- product revenue uses buy price by default;
- Fuzzworks buy max is exposed as buy price and sell min is exposed as sell price;
- material and product prices can use different market locations;
- material and product price strategies are configurable, including instant order and order-posting strategies;
- SDE `basePrice` is only a fallback when Fuzzworks has no usable data;
- final product, component, and reaction jobs can use separate facility profiles;
- final manufacturing uses the selected blueprint ME/TE, recursively built manufacturing components default to ME 10 / TE 20 and can be overridden per blueprint in the production tree;
- reaction formulas always use ME 0 / TE 0 and the selected reaction facility;
- missing prices become warnings, not exceptions.

These assumptions must be compared against `Blueprint.vb`, `ManufacturingFacility.vb`, and `Materials.vb` before expanding to exact EVE IPH behavior.

## Next Milestones

1. Extract real calculation rules from legacy `frmMain.RunBlueprint` and `Blueprint.BuildItems`.
2. Add regression fixtures for known blueprint outputs.
3. Add a cached/indexed SDE loader so startup does not repeatedly parse large YAML files.
4. Add configurable facility profiles and system cost indexes.
5. Replace manual solar system entry with an SDE-backed solar system picker.
6. Add manual price overrides and a cache refresh command.
6. Add invention/copy/reaction/reprocessing behavior as separate tested modules.
7. Add ESI SSO only after the local calculation workflow is stable.

## Verification

Use:

```powershell
dotnet build EveIndustryPlanner.sln
dotnet test EveIndustryPlanner.sln
dotnet run --project src/EveIndustryPlanner.App
```
