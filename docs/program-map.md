# Program Map

This map is based on static inspection of the repository. It is meant as a working orientation guide for future fixes and refactors, not as a complete line-by-line specification.

## Solution Overview

The repository is a legacy VB.NET Windows Forms application for EVE Isk per Hour. The main solution file is `EVE Isk per Hour.sln`.

Projects:

- `EVE Isk per Hour.vbproj`: main WinForms application, `.NET Framework 4.6.1`, root namespace `EVE_Isk_per_Hour`, output assembly `EVE Isk per Hour`.
- `EVE-IPH-Update-Program/EVEIPH Updater.vbproj`: updater application that downloads release files and migrates/copies user data between database versions.
- `EVEIPH SQLite DLL Updater/EVEIPH SQLite DLL Updater.vbproj`: auxiliary utility for replacing SQLite runtime DLLs when files may be locked.

The main project is configured primarily for `x86`; solution Any CPU configurations map back to x86. Debug and Release x86 builds output to `..\Root Directory\`.

## Startup Flow

The VB application model starts `EVE_Isk_per_Hour.My.MyApplication`, with `frmMain` as the main form and `SplashScreen` as the splash screen. `ApplicationEvents.vb` handles unhandled exceptions by writing to the log and showing `frmError`.

Most startup work happens in `frmMain.New()`:

1. Sets developer/test flags from `Developer.txt` and `Test.txt` beside the executable.
2. Initializes `ESIErrorHandler`, TLS 1.2, and `en-US` culture.
3. Chooses runtime paths. If `EVEIPH DB.sqlite` exists beside the executable, the app runs in single-folder mode. Otherwise it uses `%APPDATA%\EVE IPH`.
4. Loads application settings through `ProgramSettings`.
5. Optionally checks for updates through `ProgramUpdater`.
6. Opens the SQLite database through `DBConnection`.
7. Loads per-tab settings and saved UI state.
8. Loads the selected/default character, skills, optional ESI market data, industry cost indexes, and public structure data.
9. On first program start, initializes default market prices.

`frmMain_Load` then finishes UI setup after the constructor-level data initialization.

## Runtime State And Globals

`Globals.vb` defines the central global state in `Public_Variables`. Important values include:

- `EVEDB As DBConnection`: shared SQLite connection.
- `DBCommand As SQLiteCommand`: shared command variable used throughout the app.
- `SelectedCharacter`, `SelectedBlueprint`, and `TotalShoppingList`.
- runtime paths: `DynamicFilePath`, `DBFilePath`, `DynamicAppDataPath`, `SettingsFolder`.
- update URLs, patch notes URL, ESI constants, market constants, input character filters, and app-wide flags.

This module also contains broad utility functions for logging, formatting database strings, downloading files, JSON retrieval, progress-bar updates, and general UI helpers. Because it is globally shared, changes here have high blast radius.

## Data Storage

Primary storage is SQLite through `System.Data.SQLite`. `DBConnection.vb` owns the SQLite connection, opens the database, sets pragmas, and exposes transaction helpers. The codebase uses many raw SQL strings directly in domain classes and forms.

Important database-backed areas include:

- character/account data: `ESI_CHARACTER_DATA`, corporation tables, roles, token fields.
- ESI cache dates and status tables.
- assets, blueprints, skills, standings, research agents, loyalty points, industry jobs.
- market data: `ITEM_PRICES`, `ITEM_PRICES_FACT`, `MARKET_ORDERS`, `MARKET_HISTORY`, cache tables.
- facilities and structures: saved facilities, public structures, structure market orders, industry cost indexes.

User settings are stored as XML files under the app data `Settings` folder through `ProgramSettings.vb`. `app.config` contains .NET runtime settings, logging configuration, binding redirects, and unsafe HTTP header parsing for older server behavior.

## Main UI Surface

`frmMain.vb` is the main application shell and the largest coordination file. It owns menu handlers, tab initialization, tab settings, major workflow buttons, and many direct SQL/UI interactions.

Main functional tabs and windows include:

- Blueprint/manufacturing calculations: `frmMain.vb`, `Blueprint.vb`, `ManufacturingFacility.vb`, `Materials.vb`, `Material.vb`.
- Price updates and market history: `frmMain.vb`, `MarketPriceInterface.vb`, `FuzzworksMarket.vb`, `EVEMarketer.vb`, ESI market functions.
- Assets and blueprints from ESI: `EVEAssets.vb`, `EVEBlueprints.vb`, `frmAssetsViewer.vb`, `frmBlueprintManagement.vb`, `frmBlueprintList.vb`.
- Industry jobs: `EVEIndustryJobs.vb`, `frmIndustryJobsViewer.vb`.
- Shopping list: `ShoppingList.vb`, `frmShoppingList.vb`.
- Reprocessing and ore conversion: `ReprocessingPlant.vb`, `ConvertToOre.vb`, `frmReprocessingPlant.vb`, `frmConversiontoOreSettings.vb`.
- Account and character management: `Character.vb`, `Corporation.vb`, `frmAddCharacter.vb`, `frmManageAccounts.vb`, `frmCharacterSkills.vb`, `frmCharacterStandings.vb`.
- Structures and Upwell fitting: `StructureProcessor.vb`, `frmUpwellStructureFitting.vb`, `frmViewSavedStructures.vb`, `ManufacturingFacility.vb`.

Designer-generated files follow the WinForms pattern: `*.vb`, `*.Designer.vb`, and `*.resx`.

## Domain Modules

`Blueprint.vb` is the core manufacturing calculation model. It loads blueprint metadata, builds raw/component material lists, applies ME/TE, invention, copy/reaction costs, facility modifiers, taxes, broker fees, excess materials, and market prices. It exposes many getters used by `frmMain` to render calculated results.

`ManufacturingFacility.vb` combines a user control and domain model. The top-level `ManufacturingFacility` control drives facility selection, system/region searches, manual modifiers, cost indexes, refining settings, and saved defaults. The nested `IndustryFacility` class stores facility identity, activity, bonuses, taxes, and fitting-derived modifiers.

`MarketPriceInterface.vb` coordinates threaded ESI market order/history updates, cache checks, cancellation flags, progress updates, and post-processing of station/region data.

`EVEAssets.vb`, `EVEBlueprints.vb`, `EVEIndustryJobs.vb`, `EVESkillList.vb`, `EVEResearchAgents.vb`, `EVELoyaltyPoints.vb`, and `EVENPCStandings.vb` follow a similar pattern: load from SQLite, optionally refresh from ESI, then update local tables and cache dates.

`Materials.vb`, `Material.vb`, `ShoppingList.vb`, and `BuildBuyItems.vb` are local collection/value models used heavily by manufacturing and shopping-list workflows.

## External Integrations

`ESI.vb` handles EVE Online SSO and ESI calls. It uses PKCE-style authorization, validates JWT data, stores/refreshes access tokens in SQLite, and wraps both public and private endpoint access.

Key ESI areas:

- character identity, skills, standings, LP, research agents, ship location.
- personal and corporation blueprints, assets, industry jobs.
- corporation data, roles, divisions.
- structures, public structures, public contracts, public contract items.
- market orders, market history, adjusted/average prices, industry system cost indexes.
- ESI status route checks and centralized ESI error processing.

Market fallback/providers:

- `FuzzworksMarket.vb`: reads aggregate prices from `https://market.fuzzwork.co.uk/aggregates/`.
- `EVEMarketer.vb`: reads market stats from `https://api.evemarketer.com/ec/marketstat/json`.

Updates and patch notes use GitHub raw URLs under the `EVEIPH/LatestFiles` repository.

## Updater Flow

`ProgramUpdater.vb` belongs to the main app. It downloads the latest update XML, compares MD5 hashes, downloads a newer updater if needed, launches `EVEIPH Updater.exe`, and exits the main application.

`EVE-IPH-Update-Program/frmUpdaterMain.vb` is the real updater. It downloads files from the XML manifest, copies them into the runtime folder, handles old/new SQLite database files, and contains many table-specific migration routines such as `UpdateESICharacterDataTable`, `UpdateAssetsTable`, `UpdateMarketOrdersTable`, and `UpdateIndustrySystemCostIndiciesTable`.

## Build And Dependencies

Primary packages are listed in `packages.config`:

- `System.Data.SQLite.Core` and `Stub.System.Data.SQLite.Core.NetFramework`.
- `Newtonsoft.Json`.
- `LpSolveDotNet` plus native x86 binaries.

Typical build:

```powershell
nuget restore "EVE Isk per Hour.sln"
msbuild "EVE Isk per Hour.sln" /p:Configuration=Debug /p:Platform=x86
```

There is no dedicated unit test project in the solution. Current verification is build plus manual testing of affected WinForms workflows.

## High-Risk Areas

- `frmMain.vb` mixes UI, workflow orchestration, SQL, and domain state. Small changes can affect multiple tabs.
- `Globals.vb` contains shared mutable state and utility methods used everywhere.
- SQLite access is mostly raw SQL and often shares global `DBCommand`; review concurrency and escaping carefully.
- ESI token refresh, cache dates, and update throttling are cross-cutting and can silently alter startup behavior.
- Updater/database migration code duplicates some helpers and manipulates runtime files directly.

## Where To Start For Common Changes

- Manufacturing result wrong: inspect `frmMain.RunBlueprint`, `Blueprint.vb`, `ManufacturingFacility.vb`, `Materials.vb`.
- Market price issue: inspect `frmMain` price tab handlers, `MarketPriceInterface.vb`, `ESI.UpdateMarketOrders`, `ESI.UpdateMarketHistory`, `FuzzworksMarket.vb`.
- Character/account issue: inspect `Character.vb`, `Corporation.vb`, `ESI.SetCharacterData`, account forms, and `ESI_CHARACTER_DATA`.
- Asset/blueprint import issue: inspect `EVEAssets.vb`, `EVEBlueprints.vb`, `EVEIndustryJobs.vb`, plus relevant ESI methods.
- Settings issue: inspect `ProgramSettings.vb` and the XML file name used by the corresponding `Load*Settings` and `Save*Settings` methods.
- Release/update issue: inspect `ProgramUpdater.vb` first, then `EVE-IPH-Update-Program/frmUpdaterMain.vb`.
