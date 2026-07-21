using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using EveIndustryPlanner.App;
using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IBlueprintRepository blueprintRepository;
    private readonly IBlueprintCatalogProvider? blueprintCatalogProvider;
    private readonly ISolarSystemRepository solarSystemRepository;
    private readonly IIndustryCostIndexProvider industryCostIndexProvider;
    private readonly IManufacturingCalculator calculator;
    private readonly IShoppingListService shoppingListService;
    private readonly UserSettingsService userSettingsService;
    private readonly DataFreshnessService dataFreshnessService;
    private readonly EveSsoCharacterAuthService characterAuthService = new();
    private readonly EveAssetService assetService;
    private readonly PocketBaseDataService dataService;
    private readonly List<FacilityRigOption> allFacilityRigs = [];
    private readonly Dictionary<BlueprintId, BlueprintEfficiencySettings> blueprintEfficiencyOverrides = [];

    private string searchText = string.Empty;
    private BlueprintSearchResult? selectedBlueprint;
    private int runs = 1;
    private int lines = 1;
    private int materialEfficiency = 10;
    private int timeEfficiency = 20;
    private decimal additionalCosts;
    private MarketLocationOption selectedMaterialMarket;
    private MarketLocationOption selectedProductMarket;
    private MarketPriceStrategyOption selectedMaterialPriceStrategy;
    private MarketPriceStrategyOption selectedProductPriceStrategy;
    private BuildBuyDepthOption selectedBuildBuyDepth;
    private bool enableBuildBuy;
    private int maxBuildBuyDepth = 6;
    private FacilityOption selectedFinalProductFacility;
    private FacilityOption selectedComponentFacility;
    private FacilityOption selectedReactionFacility;
    private FacilityOption? selectedEditableFacility;
    private string facilityName = "New Facility";
    private string facilitySolarSystemName = "Jita";
    private long facilitySolarSystemId = 30000142;
    private FacilityStructureOption selectedFacilityStructure;
    private FacilityServiceOption selectedFacilityService;
    private FacilitySecurityOption selectedFacilitySecurity;
    private bool hasManufacturingPlant = true;
    private bool hasCapitalShipyard;
    private bool hasSupercapitalShipyard;
    private bool hasCompositeReactor;
    private bool hasHybridReactor;
    private bool hasBiochemicalReactor;
    private bool hasInventionLab;
    private bool hasResearchLab;
    private bool hasHyasyodaResearchLab;
    private bool hasReprocessingFacility;
    private FacilityRigOption selectedFacilityRigSlot1 = EmptyRigOption;
    private FacilityRigOption selectedFacilityRigSlot2 = EmptyRigOption;
    private FacilityRigOption selectedFacilityRigSlot3 = EmptyRigOption;
    private string solarSystemSearchText = "Jita";
    private SolarSystemOption? selectedSolarSystem;
    private decimal facilityMaterialMultiplier = 1m;
    private decimal facilityTimeMultiplier = 1m;
    private decimal facilityJobCostMultiplier = 1m;
    private decimal facilitySystemCostIndex = 0.04m;
    private decimal facilityTaxRate;
    private int selectedWorkspaceTab;
    private string facilityCostIndexStatus = "Cost index uses ESI when available.";
    private bool isBusy;
    private bool isApplyingServerData;
    private CancellationTokenSource? productionLedgerAutoSave;
    private string statusText = "Ready";
    private ManufacturingResult? result;
    private ShoppingList? shoppingList;
    private ShoppingList productionLedgerShoppingList = new();
    private readonly Dictionary<TypeId, long> productionLedgerAssetDeductions = new();
    private readonly Dictionary<Guid, IReadOnlyList<ManufacturingResult>> productionLedgerSplitResults = new();
    private ProductionLedgerEntry? selectedProductionLedgerEntry;
    private Dictionary<string, ProductionJobCompletionState> productionJobCompletionStates = new(StringComparer.Ordinal);
    private ProductionLedgerOption? selectedProductionLedger;
    private string productionLedgerName = "Production Ledger";
    private ProductionPlannerScopeOption selectedProductionPlannerScope;
    private ProductionJobFilterOption selectedProductionJobFilter;
    private decimal maxManufacturingJobHours = 48m;
    private decimal maxReactionJobHours = 24m;
    private int maxParallelManufacturingJobs = 10;
    private int maxParallelReactionJobs = 5;
    private CharacterAccountOption? selectedCharacterAccount;
    private string pocketBaseUrl = string.Empty;
    private MarketScannerFilterOption selectedMarketScannerFilter;
    private MarketScannerTechFilterOption selectedMarketScannerTechFilter;
    private int marketScannerMaxItems = 100;
    private bool isMarketScannerBusy;
    private string marketScannerStatusText = "Scanner ready.";
    private string marketScannerProgressText = string.Empty;
    private readonly Dictionary<string, MarketScannerCacheEntry> marketScannerCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? efficiencyRecalculation;
    private string sdeFreshnessText = "SDE: checking";
    private string marketFreshnessText = "Market: checking";
    private string costIndexFreshnessText = "Cost index: checking";

    private static readonly IReadOnlyList<string> DefaultCharacterScopes =
    [
        "esi-skills.read_skills.v1",
        "esi-universe.read_structures.v1",
        "esi-markets.structure_markets.v1",
        "esi-characters.read_standings.v1",
        "esi-industry.read_character_jobs.v1",
        "esi-characters.read_agents_research.v1",
        "esi-assets.read_assets.v1",
        "esi-characters.read_blueprints.v1",
        "esi-planets.manage_planets.v1",
        "esi-corporations.read_corporation_membership.v1",
        "esi-industry.read_corporation_jobs.v1",
        "esi-assets.read_corporation_assets.v1",
        "esi-corporations.read_blueprints.v1",
        "esi-wallet.read_corporation_wallets.v1",
        "esi-markets.read_corporation_orders.v1",
        "esi-corporations.read_divisions.v1",
        "esi-location.read_ship_type.v1",
        "esi-location.read_location.v1",
        "esi-characters.read_loyalty.v1",
        "esi-wallet.read_character_wallet.v1",
        "esi-markets.read_character_orders.v1",
        "esi-ui.open_window.v1"
    ];

    public MainWindowViewModel()
        : this(CreateDefaultServices())
    {
    }

    public MainWindowViewModel(SavedCharacterAccount authenticatedAccount)
        : this()
    {
        var account = CharacterAccounts.FirstOrDefault(character => character.CharacterId == authenticatedAccount.CharacterId);
        if (account is null)
        {
            account = CharacterAccountOption.FromSaved(authenticatedAccount);
            CharacterAccounts.Add(account);
        }

        SelectedCharacterAccount = account;
    }

    private MainWindowViewModel(DefaultServices services)
        : this(services.BlueprintRepository, services.SolarSystemRepository, services.IndustryCostIndexProvider, services.Calculator, services.ShoppingListService, services.UserSettingsService)
    {
        LoadFacilityRigOptions(services.FacilityRigPresets);
        RecalculateFacilityMultipliers();
        StatusText = services.StatusText;
    }

    public MainWindowViewModel(
        IBlueprintRepository blueprintRepository,
        ISolarSystemRepository solarSystemRepository,
        IIndustryCostIndexProvider industryCostIndexProvider,
        IManufacturingCalculator calculator,
        IShoppingListService shoppingListService,
        UserSettingsService? userSettingsService = null)
    {
        this.blueprintRepository = blueprintRepository;
        blueprintCatalogProvider = blueprintRepository as IBlueprintCatalogProvider;
        this.solarSystemRepository = solarSystemRepository;
        this.industryCostIndexProvider = industryCostIndexProvider;
        this.calculator = calculator;
        this.shoppingListService = shoppingListService;
        this.userSettingsService = userSettingsService ?? new UserSettingsService(GetSettingsFilePath());
        dataFreshnessService = new DataFreshnessService(
            FindSdeDirectory() ?? SdeDownloadService.DefaultSdeDirectory,
            GetMarketCacheFilePath(),
            GetIndustryCostIndexCacheFilePath());
        assetService = new EveAssetService(() => PocketBaseUrl);
        dataService = new PocketBaseDataService(() => PocketBaseUrl, () => SelectedCharacterAccount?.PocketBaseAuthToken ?? CharacterAccounts.FirstOrDefault()?.PocketBaseAuthToken ?? string.Empty);

        selectedFacilityStructure = FacilityStructures.First(option => option.Id == "raitaru");
        selectedFacilityService = CostIndexActivities.First(option => option.Role == FacilityServiceRole.Manufacturing);
        selectedFacilitySecurity = FacilitySecurityBands.First(option => option.SecurityBand == FacilitySecurityBand.HighSec);
        LoadFacilityRigOptions(FacilityFittingCalculator.RigPresets);

        var settings = this.userSettingsService.Load();
        selectedMaterialMarket = FindMarketLocation(settings.MaterialMarketLocationId);
        selectedProductMarket = FindMarketLocation(settings.ProductMarketLocationId);
        selectedMaterialPriceStrategy = FindMaterialPriceStrategy(settings.MaterialPriceSelection);
        selectedProductPriceStrategy = FindProductPriceStrategy(settings.ProductPriceSelection);
        selectedBuildBuyDepth = FindBuildBuyDepth(settings.BuildBuyDepth);
        selectedProductionPlannerScope = ProductionPlannerScopes.First(option => option.Scope == ProductionPlannerScope.AllLedgerItems);
        selectedProductionJobFilter = ProductionJobFilters.First(option => option.Filter == ProductionJobFilter.Open);
        selectedMarketScannerFilter = MarketScannerFilters.First(option => option.Filter == MarketScannerFilter.All);
        selectedMarketScannerTechFilter = MarketScannerTechFilters.First(option => option.Filter == MarketScannerTechFilter.All);
        enableBuildBuy = settings.EnableBuildBuy;
        maxBuildBuyDepth = Math.Clamp(settings.MaxBuildBuyDepth, 0, 20);
        LoadFacilityProfiles(settings);
        LoadCharacterAccounts(settings);
        pocketBaseUrl = string.IsNullOrWhiteSpace(settings.PocketBaseUrl)
            ? UserSettings.DefaultPocketBaseUrl
            : settings.PocketBaseUrl;
        selectedFinalProductFacility = FindFacility(settings.FinalProductFacilityId);
        selectedComponentFacility = FindFacility(settings.ComponentFacilityId);
        selectedReactionFacility = FindFacility(settings.ReactionFacilityId);

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        CalculateCommand = new AsyncRelayCommand(CalculateAsync, () => SelectedBlueprint is not null && !IsBusy);
        CopyShoppingListCommand = new RelayCommand(CopyShoppingList, () => ShoppingListText.Length > 0);
        AddToProductionCommand = new RelayCommand(AddToProductionLedger, () => Result is not null);
        RemoveProductionEntryCommand = new RelayCommand(RemoveSelectedProductionEntry, () => SelectedProductionLedgerEntry is not null);
        ClearProductionLedgerCommand = new RelayCommand(ClearProductionLedger, () => ProductionLedgerEntries.Count > 0);
        CopyProductionShoppingListCommand = new RelayCommand(CopyProductionShoppingList, () => ProductionLedgerShoppingListText.Length > 0);
        UseProductionAssetsCommand = new RelayCommand(UseProductionAssets, () => ProductionLedgerEntries.Count > 0 && CharacterAccounts.Count > 0);
        NewProductionLedgerCommand = new RelayCommand(NewProductionLedger);
        SaveProductionLedgerCommand = new AsyncRelayCommand(SaveProductionLedgerAsync, () => ProductionLedgerEntries.Count > 0 && SelectedCharacterAccount is not null);
        LoadProductionLedgerCommand = new AsyncRelayCommand(LoadProductionLedgerAsync, () => SelectedCharacterAccount is not null);
        DeleteProductionLedgerCommand = new AsyncRelayCommand(DeleteProductionLedgerAsync, () => SelectedProductionLedger is not null && SelectedCharacterAccount is not null);
        RecalculateProductionPlanCommand = new AsyncRelayCommand(RecalculateProductionLedgerPlanningAsync, () => ProductionLedgerEntries.Count > 0 && !IsBusy);
        SaveFacilityCommand = new RelayCommand(SaveFacility);
        NewFacilityCommand = new RelayCommand(NewFacility);
        DeleteFacilityCommand = new RelayCommand(DeleteFacility, () => CanDeleteSelectedFacility);
        SearchSolarSystemsCommand = new AsyncRelayCommand(SearchSolarSystemsAsync);
        RefreshCostIndexCommand = new AsyncRelayCommand(RefreshFacilityCostIndexAsync);
        AddCharacterCommand = new AsyncRelayCommand(AddCharacterAsync, () => !IsBusy);
        DeleteCharacterCommand = new RelayCommand(DeleteSelectedCharacter, () => SelectedCharacterAccount is not null);
        RunMarketScannerCommand = new AsyncRelayCommand(RunMarketScannerAsync, () => !IsBusy && !IsMarketScannerBusy && blueprintCatalogProvider is not null);
        RefreshSdeCommand = new AsyncRelayCommand(RefreshSdeAsync, () => !IsBusy);
        RefreshDataFreshness();
    }

    public async Task InitializeAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Loading SDE blueprint data");
        await SearchAsync();

        progress?.Report("Loading solar system data");
        await SearchSolarSystemsAsync();

        progress?.Report("Loading server-synced planner data");
        await LoadServerDataAsync();
    }

    public ObservableCollection<BlueprintSearchResult> Blueprints { get; } = [];

    public ObservableCollection<MaterialRequirement> Materials { get; } = [];

    public ObservableCollection<ProductionTreeNodeViewModel> ProductionTree { get; } = [];

    public ObservableCollection<ProductionLedgerEntry> ProductionLedgerEntries { get; } = [];

    public ObservableCollection<ProductionLedgerOption> ProductionLedgers { get; } = [];

    public ObservableCollection<ShoppingListLine> ProductionLedgerMaterials { get; } = [];

    public ObservableCollection<PlannedProductionJobRow> ProductionJobPlanRows { get; } = [];

    public ObservableCollection<FacilityOption> FacilityProfiles { get; } = [];

    public ObservableCollection<SolarSystemOption> SolarSystemResults { get; } = [];

    public ObservableCollection<CharacterAccountOption> CharacterAccounts { get; } = [];

    public ObservableCollection<MarketScannerResultRow> MarketScannerResults { get; } = [];

    private static FacilityRigOption EmptyRigOption { get; } =
        new(FacilityFittingCalculator.NoneRigId, "Empty Slot", FacilityRigSize.None, FacilityServiceRole.Manufacturing, string.Empty);

    public IReadOnlyList<MarketLocationOption> MarketLocations { get; } =
    [
        new("The Forge", 10000002),
        new("Jita", 30000142),
        new("Perimeter", 30000144),
        new("Jita 4-4 CNAP", 60003760),
        new("Amarr VIII", 60008494),
        new("Dodixie", 60011866),
        new("Rens", 60004588),
        new("Hek", 60005686),
        new("Global", 0)
    ];

    public IReadOnlyList<MarketPriceStrategyOption> MaterialPriceStrategies { get; } =
    [
        new("Instant Buy", MarketPriceSelection.InstantBuy),
        new("Buy Order", MarketPriceSelection.BuyOrder),
        new("Sell Median", MarketPriceSelection.SellMedian),
        new("Sell Percentile", MarketPriceSelection.SellPercentile),
        new("Sell Weighted Avg", MarketPriceSelection.SellWeightedAverage)
    ];

    public IReadOnlyList<MarketPriceStrategyOption> ProductPriceStrategies { get; } =
    [
        new("Instant Sell", MarketPriceSelection.InstantSell),
        new("Sell Order", MarketPriceSelection.SellOrder),
        new("Buy Median", MarketPriceSelection.BuyMedian),
        new("Buy Percentile", MarketPriceSelection.BuyPercentile),
        new("Buy Weighted Avg", MarketPriceSelection.BuyWeightedAverage),
        new("Sell Median", MarketPriceSelection.SellMedian)
    ];

    public IReadOnlyList<BuildBuyDepthOption> BuildBuyDepthOptions { get; } =
    [
        new("Direct Materials Only", BuildBuyDepth.DirectMaterialsOnly),
        new("Build Components", BuildBuyDepth.BuildManufacturingComponents),
        new("Build Components + Reactions", BuildBuyDepth.BuildManufacturingAndReactions)
    ];

    public IReadOnlyList<MarketScannerFilterOption> MarketScannerFilters { get; } =
    [
        new("All", MarketScannerFilter.All),
        new("Reactions", MarketScannerFilter.Reactions),
        new("Components", MarketScannerFilter.Components),
        new("Ships", MarketScannerFilter.Ships),
        new("Modules", MarketScannerFilter.Modules),
        new("Drones / Fighters", MarketScannerFilter.DronesAndFighters),
        new("Charges", MarketScannerFilter.Charges),
        new("Structures", MarketScannerFilter.Structures),
        new("Other", MarketScannerFilter.Other)
    ];

    public IReadOnlyList<MarketScannerTechFilterOption> MarketScannerTechFilters { get; } =
    [
        new("All", MarketScannerTechFilter.All),
        new("Tech I", MarketScannerTechFilter.Tech1),
        new("Tech II", MarketScannerTechFilter.Tech2),
        new("Tech III", MarketScannerTechFilter.Tech3),
        new("Faction", MarketScannerTechFilter.Faction),
        new("Storyline", MarketScannerTechFilter.Storyline),
        new("Officer", MarketScannerTechFilter.Officer),
        new("Deadspace", MarketScannerTechFilter.Deadspace),
        new("Other Meta", MarketScannerTechFilter.OtherMeta)
    ];

    public IReadOnlyList<ProductionPlannerScopeOption> ProductionPlannerScopes { get; } =
    [
        new("All Ledger Items", ProductionPlannerScope.AllLedgerItems),
        new("Selected Item", ProductionPlannerScope.SelectedLedgerItem)
    ];

    public IReadOnlyList<ProductionJobFilterOption> ProductionJobFilters { get; } =
    [
        new("Open", ProductionJobFilter.Open),
        new("All", ProductionJobFilter.All),
        new("Completed", ProductionJobFilter.Completed)
    ];

    public IReadOnlyList<FacilityStructureOption> FacilityStructures { get; } =
        FacilityFittingCalculator.StructurePresets
            .Select(preset => new FacilityStructureOption(preset.Id, preset.Name, preset.RigSlots, preset.RigSize))
            .ToList();

    public IReadOnlyList<FacilityServiceOption> CostIndexActivities { get; } =
    [
        new("manufacturing", "Manufacturing", FacilityServiceRole.Manufacturing),
        new("reactions", "Reactions", FacilityServiceRole.Reactions)
    ];

    public IReadOnlyList<FacilitySecurityOption> FacilitySecurityBands { get; } =
    [
        new("High Sec", FacilitySecurityBand.HighSec),
        new("Low Sec", FacilitySecurityBand.LowSec),
        new("Null / WH", FacilitySecurityBand.NullSec)
    ];

    public ObservableCollection<FacilityRigOption> FacilityRigs { get; } = [];

    public ICommand SearchCommand { get; }

    public ICommand CalculateCommand { get; }

    public ICommand CopyShoppingListCommand { get; }

    public ICommand AddToProductionCommand { get; }

    public ICommand RemoveProductionEntryCommand { get; }

    public ICommand ClearProductionLedgerCommand { get; }

    public ICommand CopyProductionShoppingListCommand { get; }

    public ICommand UseProductionAssetsCommand { get; }

    public ICommand NewProductionLedgerCommand { get; }

    public ICommand SaveProductionLedgerCommand { get; }

    public ICommand LoadProductionLedgerCommand { get; }

    public ICommand DeleteProductionLedgerCommand { get; }

    public ICommand RecalculateProductionPlanCommand { get; }

    public ICommand SaveFacilityCommand { get; }

    public ICommand NewFacilityCommand { get; }

    public ICommand DeleteFacilityCommand { get; }

    public ICommand SearchSolarSystemsCommand { get; }

    public ICommand RefreshCostIndexCommand { get; }

    public ICommand AddCharacterCommand { get; }

    public ICommand DeleteCharacterCommand { get; }

    public ICommand RunMarketScannerCommand { get; }

    public ICommand RefreshSdeCommand { get; }

    public string SdeFreshnessText
    {
        get => sdeFreshnessText;
        private set => SetProperty(ref sdeFreshnessText, value);
    }

    public string MarketFreshnessText
    {
        get => marketFreshnessText;
        private set => SetProperty(ref marketFreshnessText, value);
    }

    public string CostIndexFreshnessText
    {
        get => costIndexFreshnessText;
        private set => SetProperty(ref costIndexFreshnessText, value);
    }

    public int SelectedWorkspaceTab
    {
        get => selectedWorkspaceTab;
        set => SetProperty(ref selectedWorkspaceTab, Math.Clamp(value, 0, 4));
    }

    public string SearchText
    {
        get => searchText;
        set => SetProperty(ref searchText, value);
    }

    public BlueprintSearchResult? SelectedBlueprint
    {
        get => selectedBlueprint;
        set
        {
            if (SetProperty(ref selectedBlueprint, value))
            {
                ProductionTree.Clear();
                if (value is not null)
                {
                    MaterialEfficiency = value.ActivityType == BlueprintActivityType.Reaction ? 0 : 10;
                    TimeEfficiency = value.ActivityType == BlueprintActivityType.Reaction ? 0 : 20;
                }

                OnPropertyChanged(nameof(IsFinalEfficiencyEditable));
                RaiseCommandStates();
            }
        }
    }

    public bool IsFinalEfficiencyEditable => SelectedBlueprint?.ActivityType != BlueprintActivityType.Reaction;

    public int Runs
    {
        get => runs;
        set => SetProperty(ref runs, Math.Max(1, value));
    }

    public int Lines
    {
        get => lines;
        set => SetProperty(ref lines, Math.Max(1, value));
    }

    public int MaterialEfficiency
    {
        get => materialEfficiency;
        set => SetProperty(ref materialEfficiency, Math.Clamp(value, 0, 10));
    }

    public int TimeEfficiency
    {
        get => timeEfficiency;
        set => SetProperty(ref timeEfficiency, Math.Clamp(value, 0, 20));
    }

    public decimal AdditionalCosts
    {
        get => additionalCosts;
        set => SetProperty(ref additionalCosts, Math.Max(0m, value));
    }

    public MarketLocationOption SelectedMaterialMarket
    {
        get => selectedMaterialMarket;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedMaterialMarket, value))
            {
                SaveUserSettings();
            }
        }
    }

    public MarketLocationOption SelectedProductMarket
    {
        get => selectedProductMarket;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedProductMarket, value))
            {
                SaveUserSettings();
            }
        }
    }

    public MarketPriceStrategyOption SelectedMaterialPriceStrategy
    {
        get => selectedMaterialPriceStrategy;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedMaterialPriceStrategy, value))
            {
                SaveUserSettings();
            }
        }
    }

    public MarketPriceStrategyOption SelectedProductPriceStrategy
    {
        get => selectedProductPriceStrategy;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedProductPriceStrategy, value))
            {
                SaveUserSettings();
            }
        }
    }

    public bool EnableBuildBuy
    {
        get => enableBuildBuy;
        set
        {
            if (SetProperty(ref enableBuildBuy, value))
            {
                SaveUserSettings();
            }
        }
    }

    public BuildBuyDepthOption SelectedBuildBuyDepth
    {
        get => selectedBuildBuyDepth;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedBuildBuyDepth, value))
            {
                SaveUserSettings();
            }
        }
    }

    public int MaxBuildBuyDepth
    {
        get => maxBuildBuyDepth;
        set
        {
            if (SetProperty(ref maxBuildBuyDepth, Math.Clamp(value, 0, 20)))
            {
                SaveUserSettings();
            }
        }
    }

    public FacilityOption SelectedFinalProductFacility
    {
        get => selectedFinalProductFacility;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedFinalProductFacility, value))
            {
                OnPropertyChanged(nameof(SelectedFinalProductFacilityText));
                SaveUserSettings();
            }
        }
    }

    public FacilityOption SelectedComponentFacility
    {
        get => selectedComponentFacility;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedComponentFacility, value))
            {
                OnPropertyChanged(nameof(SelectedComponentFacilityText));
                SaveUserSettings();
            }
        }
    }

    public FacilityOption SelectedReactionFacility
    {
        get => selectedReactionFacility;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedReactionFacility, value))
            {
                OnPropertyChanged(nameof(SelectedReactionFacilityText));
                SaveUserSettings();
            }
        }
    }

    public string SelectedFinalProductFacilityText => SelectedFinalProductFacility.DisplayName;

    public string SelectedComponentFacilityText => SelectedComponentFacility.DisplayName;

    public string SelectedReactionFacilityText => SelectedReactionFacility.DisplayName;

    public FacilityOption? SelectedEditableFacility
    {
        get => selectedEditableFacility;
        set
        {
            if (SetProperty(ref selectedEditableFacility, value))
            {
                if (value is not null)
                {
                    LoadFacilityIntoEditor(value);
                }

                OnPropertyChanged(nameof(CanDeleteSelectedFacility));
                RaiseFacilityEditCommandStates();
            }
        }
    }

    public bool CanDeleteSelectedFacility =>
        SelectedEditableFacility is not null && SelectedEditableFacility.Id != SavedFacilityProfile.NoneId;

    public string FacilityName
    {
        get => facilityName;
        set => SetProperty(ref facilityName, value);
    }

    public string FacilitySolarSystemName
    {
        get => facilitySolarSystemName;
        set => SetProperty(ref facilitySolarSystemName, value);
    }

    public long FacilitySolarSystemId
    {
        get => facilitySolarSystemId;
        set => SetProperty(ref facilitySolarSystemId, Math.Max(0, value));
    }

    public string SolarSystemSearchText
    {
        get => solarSystemSearchText;
        set => SetProperty(ref solarSystemSearchText, value);
    }

    public SolarSystemOption? SelectedSolarSystem
    {
        get => selectedSolarSystem;
        set
        {
            if (SetProperty(ref selectedSolarSystem, value) && value is not null)
            {
                FacilitySolarSystemName = value.Name;
                FacilitySolarSystemId = value.SolarSystemId;
                SelectedFacilitySecurity = FindSecurityBand(value.SecurityStatus);
                _ = RefreshFacilityCostIndexAsync();
            }
        }
    }

    public FacilityStructureOption SelectedFacilityStructure
    {
        get => selectedFacilityStructure;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedFacilityStructure, value))
            {
                RefreshFacilityRigOptions();
                OnPropertyChanged(nameof(IsFacilityRigSlot1Enabled));
                OnPropertyChanged(nameof(IsFacilityRigSlot2Enabled));
                OnPropertyChanged(nameof(IsFacilityRigSlot3Enabled));
                OnPropertyChanged(nameof(FacilityRigSlotSummary));
                RecalculateFacilityMultipliers();
            }
        }
    }

    public FacilityServiceOption SelectedFacilityService
    {
        get => selectedFacilityService;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedFacilityService, value))
            {
                RecalculateFacilityMultipliers();
                _ = RefreshFacilityCostIndexAsync();
            }
        }
    }

    public bool HasManufacturingPlant
    {
        get => hasManufacturingPlant;
        set => SetProperty(ref hasManufacturingPlant, value);
    }

    public bool HasCapitalShipyard
    {
        get => hasCapitalShipyard;
        set => SetProperty(ref hasCapitalShipyard, value);
    }

    public bool HasSupercapitalShipyard
    {
        get => hasSupercapitalShipyard;
        set => SetProperty(ref hasSupercapitalShipyard, value);
    }

    public bool HasCompositeReactor
    {
        get => hasCompositeReactor;
        set => SetProperty(ref hasCompositeReactor, value);
    }

    public bool HasHybridReactor
    {
        get => hasHybridReactor;
        set => SetProperty(ref hasHybridReactor, value);
    }

    public bool HasBiochemicalReactor
    {
        get => hasBiochemicalReactor;
        set => SetProperty(ref hasBiochemicalReactor, value);
    }

    public bool HasInventionLab
    {
        get => hasInventionLab;
        set => SetProperty(ref hasInventionLab, value);
    }

    public bool HasResearchLab
    {
        get => hasResearchLab;
        set => SetProperty(ref hasResearchLab, value);
    }

    public bool HasHyasyodaResearchLab
    {
        get => hasHyasyodaResearchLab;
        set => SetProperty(ref hasHyasyodaResearchLab, value);
    }

    public bool HasReprocessingFacility
    {
        get => hasReprocessingFacility;
        set => SetProperty(ref hasReprocessingFacility, value);
    }

    public FacilitySecurityOption SelectedFacilitySecurity
    {
        get => selectedFacilitySecurity;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedFacilitySecurity, value))
            {
                RecalculateFacilityMultipliers();
            }
        }
    }

    public FacilityRigOption SelectedFacilityRigSlot1
    {
        get => selectedFacilityRigSlot1;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedFacilityRigSlot1, value))
            {
                RecalculateFacilityMultipliers();
            }
        }
    }

    public bool IsFacilityRigSlot1Enabled => SelectedFacilityStructure.RigSlots >= 1;

    public FacilityRigOption SelectedFacilityRigSlot2
    {
        get => selectedFacilityRigSlot2;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedFacilityRigSlot2, value))
            {
                RecalculateFacilityMultipliers();
            }
        }
    }

    public bool IsFacilityRigSlot2Enabled => SelectedFacilityStructure.RigSlots >= 2;

    public FacilityRigOption SelectedFacilityRigSlot3
    {
        get => selectedFacilityRigSlot3;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedFacilityRigSlot3, value))
            {
                RecalculateFacilityMultipliers();
            }
        }
    }

    public bool IsFacilityRigSlot3Enabled => SelectedFacilityStructure.RigSlots >= 3;

    public string FacilityRigSlotSummary => SelectedFacilityStructure.RigSize switch
    {
        FacilityRigSize.Medium => "Rig Slots (M-Set)",
        FacilityRigSize.Large => "Rig Slots (L-Set)",
        FacilityRigSize.XLarge => "Rig Slots (XL-Set)",
        _ => "Rig Slots"
    };

    public decimal FacilityMaterialMultiplier
    {
        get => facilityMaterialMultiplier;
        private set => SetProperty(ref facilityMaterialMultiplier, Math.Max(0m, value));
    }

    public decimal FacilityTimeMultiplier
    {
        get => facilityTimeMultiplier;
        private set => SetProperty(ref facilityTimeMultiplier, Math.Max(0m, value));
    }

    public decimal FacilityJobCostMultiplier
    {
        get => facilityJobCostMultiplier;
        private set => SetProperty(ref facilityJobCostMultiplier, Math.Max(0m, value));
    }

    public string FacilityMultiplierSummary =>
        $"ME x{FacilityMaterialMultiplier:N4} / TE x{FacilityTimeMultiplier:N4} / Job x{FacilityJobCostMultiplier:N4}";

    public decimal FacilitySystemCostIndex
    {
        get => facilitySystemCostIndex;
        set => SetProperty(ref facilitySystemCostIndex, Math.Max(0m, value));
    }

    public string FacilityCostIndexStatus
    {
        get => facilityCostIndexStatus;
        private set => SetProperty(ref facilityCostIndexStatus, value);
    }

    public decimal FacilityTaxRate
    {
        get => facilityTaxRate;
        set => SetProperty(ref facilityTaxRate, Math.Max(0m, value));
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsMarketScannerBusy
    {
        get => isMarketScannerBusy;
        private set
        {
            if (SetProperty(ref isMarketScannerBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public MarketScannerFilterOption SelectedMarketScannerFilter
    {
        get => selectedMarketScannerFilter;
        set
        {
            if (value is not null)
            {
                SetProperty(ref selectedMarketScannerFilter, value);
            }
        }
    }

    public MarketScannerTechFilterOption SelectedMarketScannerTechFilter
    {
        get => selectedMarketScannerTechFilter;
        set
        {
            if (value is not null)
            {
                SetProperty(ref selectedMarketScannerTechFilter, value);
            }
        }
    }

    public int MarketScannerMaxItems
    {
        get => marketScannerMaxItems;
        set => SetProperty(ref marketScannerMaxItems, Math.Max(0, value));
    }

    public string MarketScannerStatusText
    {
        get => marketScannerStatusText;
        private set => SetProperty(ref marketScannerStatusText, value);
    }

    public string MarketScannerProgressText
    {
        get => marketScannerProgressText;
        private set => SetProperty(ref marketScannerProgressText, value);
    }

    public CharacterAccountOption? SelectedCharacterAccount
    {
        get => selectedCharacterAccount;
        set
        {
            if (SetProperty(ref selectedCharacterAccount, value))
            {
                OnPropertyChanged(nameof(SelectedCharacterScopesText));
                OnPropertyChanged(nameof(SelectedCharacterTokenText));
                RaiseCharacterCommandStates();
                RaiseProductionLedgerCommandStates();
            }
        }
    }

    public string SelectedCharacterScopesText =>
        SelectedCharacterAccount is null
            ? "No character selected."
            : string.Join(Environment.NewLine, SelectedCharacterAccount.Scopes.OrderBy(scope => scope, StringComparer.Ordinal));

    public string SelectedCharacterTokenText =>
        SelectedCharacterAccount is null
            ? string.Empty
            : $"PocketBase auth: {MaskToken(SelectedCharacterAccount.PocketBaseAuthToken)}{Environment.NewLine}"
              + "EVE tokens are stored and refreshed server-side.";

    public string PocketBaseUrl
    {
        get => pocketBaseUrl;
        set
        {
            if (SetProperty(ref pocketBaseUrl, value?.Trim() ?? string.Empty))
            {
                SaveUserSettings();
            }
        }
    }

    public ManufacturingResult? Result
    {
        get => result;
        private set
        {
            if (SetProperty(ref result, value))
            {
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(MaterialCostText));
                OnPropertyChanged(nameof(JobCostText));
                OnPropertyChanged(nameof(AdditionalCostsText));
                OnPropertyChanged(nameof(TotalCostText));
                OnPropertyChanged(nameof(RevenueText));
                OnPropertyChanged(nameof(ProfitText));
                OnPropertyChanged(nameof(ProfitPercentText));
                OnPropertyChanged(nameof(IskPerHourText));
                OnPropertyChanged(nameof(ProductionTimeText));
                OnPropertyChanged(nameof(FinalProductionTimeText));
                OnPropertyChanged(nameof(BuildProductionTimeText));
                OnPropertyChanged(nameof(BaseProductionTimeText));
                OnPropertyChanged(nameof(EffectiveFinalTimeMultiplierText));
                OnPropertyChanged(nameof(FinalJobCostText));
                OnPropertyChanged(nameof(BuildJobCostText));
                OnPropertyChanged(nameof(EffectiveJobCostRateText));
                OnPropertyChanged(nameof(CalculationBreakdownText));
                OnPropertyChanged(nameof(OutputText));
                OnPropertyChanged(nameof(WarningText));
                OnPropertyChanged(nameof(MaterialAuditSummaryText));
                RaiseProductionLedgerCommandStates();
            }
        }
    }

    public bool HasResult => Result is not null;

    public string MaterialCostText => FormatIsk(Result?.MaterialCost);

    public string JobCostText => FormatIsk(Result?.JobCost);

    public string AdditionalCostsText => FormatIsk(Result?.AdditionalCosts);

    public string TotalCostText => FormatIsk(Result?.TotalCost);

    public string RevenueText => FormatIsk(Result?.EstimatedRevenue);

    public string ProfitText => FormatIsk(Result?.Profit);

    public string ProfitPercentText => Result is null ? "-" : $"{Result.ProfitPercent:P2}";

    public string IskPerHourText => FormatIsk(Result?.IskPerHour);

    public string ProductionTimeText => Result is null ? "-" : FormatDuration(Result.TotalProductionTime);

    public string FinalProductionTimeText => Result is null ? "-" : FormatDuration(Result.FinalProductionTime);

    public string BuildProductionTimeText => Result is null ? "-" : FormatDuration(Result.BuildProductionTime);

    public string BaseProductionTimeText => Result is null ? "-" : FormatDuration(Result.Blueprint.BaseProductionTime);

    public string EffectiveFinalTimeMultiplierText => Result is null
        ? "-"
        : $"Blueprint TE x{1m - TimeEfficiency / 100m:N4} / Facility TE x{Result.FinalFacilityTimeMultiplier:N4}";

    public string FinalJobCostText => FormatIsk(Result?.FinalJobCost);

    public string BuildJobCostText => FormatIsk(Result?.BuildJobCost);

    public string EffectiveJobCostRateText => Result is null
        ? "-"
        : $"{Result.FinalFacilitySystemCostIndex:P4} x {Result.FinalFacilityJobCostMultiplier:N4} + {Result.FinalFacilityTaxRate:P4}";

    public string OutputText => Result is null ? "-" : $"{Result.OutputQuantity:N0} x {Result.Blueprint.ProductName}";

    public string WarningText => Result is null || Result.Warnings.Count == 0
        ? BuildBuyText
        : string.Join(Environment.NewLine, Result.Warnings.Concat(BuildBuyTextLines));

    public string MaterialAuditSummaryText => Result is null
        ? string.Empty
        : $"{Materials.Count:N0} materials | {Materials.Count(material => material.MissingPrice):N0} missing prices | {Materials.Count(material => !material.HasEnoughMarketVolume):N0} low instant-buy volume";

    public string CalculationBreakdownText => Result is null
        ? string.Empty
        : string.Join(
            Environment.NewLine,
            new[]
            {
                $"Base time: {BaseProductionTimeText}",
                $"Final product time: {FinalProductionTimeText}",
                $"Build/buy production time: {BuildProductionTimeText}",
                $"Total production time: {ProductionTimeText}",
                string.Empty,
                $"Material cost: {MaterialCostText}",
                $"Final job cost: {FinalJobCostText}",
                $"Build/buy job cost: {BuildJobCostText}",
                $"Additional costs: {AdditionalCostsText}",
                $"Total cost: {TotalCostText}",
                string.Empty,
                $"Final facility ME: x{Result.FinalFacilityMaterialMultiplier:N4}",
                $"Final facility TE: x{Result.FinalFacilityTimeMultiplier:N4}",
                $"Final job multiplier: x{Result.FinalFacilityJobCostMultiplier:N4}",
                $"System index formula: {EffectiveJobCostRateText}"
            }.Concat(WarningBreakdownLines));

    private IEnumerable<string> WarningBreakdownLines => Result?.Warnings.Count > 0
        ? new[] { string.Empty, "Warnings:" }.Concat(Result.Warnings)
        : [];

    private string BuildBuyText => string.Join(Environment.NewLine, BuildBuyTextLines);

    private IEnumerable<string> BuildBuyTextLines => Result?.BuildBuyDecisions.Count > 0
        ? Result.BuildBuyDecisions.Select(decision =>
            $"{(decision.Built ? "BUILD" : "BUY")} {decision.Name} x{decision.RequiredQuantity:N0}{FormatFacilityName(decision.FacilityName)} - buy {decision.BuyCost:N2} ISK / build {decision.BuildCost:N2} ISK")
        : [];

    public string ShoppingListText => shoppingList?.ToMultibuyText() ?? string.Empty;

    public ProductionLedgerEntry? SelectedProductionLedgerEntry
    {
        get => selectedProductionLedgerEntry;
        set
        {
            if (SetProperty(ref selectedProductionLedgerEntry, value))
            {
                OnPropertyChanged(nameof(SelectedProductionTreeText));
                OnPropertyChanged(nameof(SelectedProductionBreakdownText));
                RefreshProductionJobPlan();
                RaiseProductionLedgerCommandStates();
            }
        }
    }

    public ProductionLedgerOption? SelectedProductionLedger
    {
        get => selectedProductionLedger;
        set
        {
            if (SetProperty(ref selectedProductionLedger, value) && value is not null)
            {
                isApplyingServerData = true;
                try
                {
                    ProductionLedgerName = value.Name;
                    ApplyProductionLedgerDocument(value.Document, replaceExisting: true);
                }
                finally
                {
                    isApplyingServerData = false;
                }

                RaiseProductionLedgerCommandStates();
            }
        }
    }

    public string ProductionLedgerName
    {
        get => productionLedgerName;
        set
        {
            if (SetProperty(ref productionLedgerName, string.IsNullOrWhiteSpace(value) ? "Production Ledger" : value))
            {
                ScheduleProductionLedgerAutoSave();
            }
        }
    }

    public string ProductionLedgerShoppingListText => productionLedgerShoppingList.ToMultibuyText();

    public string ProductionLedgerSummaryText => ProductionLedgerEntries.Count == 0
        ? "No production jobs in ledger."
        : $"{ProductionLedgerEntries.Count:N0} jobs / {FormatIsk(ProductionLedgerEntries.Sum(entry => entry.Result.TotalCost))} total cost / {FormatIsk(ProductionLedgerEntries.Sum(entry => entry.Result.EstimatedRevenue))} revenue";

    public string SelectedProductionTreeText => SelectedProductionLedgerEntry is null
        ? string.Empty
        : BuildProductionTreeText(SelectedProductionLedgerEntry.Result);

    public string SelectedProductionBreakdownText => SelectedProductionLedgerEntry is null
        ? string.Empty
        : BuildProductionBreakdownText(SelectedProductionLedgerEntry);

    public ProductionPlannerScopeOption SelectedProductionPlannerScope
    {
        get => selectedProductionPlannerScope;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedProductionPlannerScope, value))
            {
                RefreshProductionJobPlan();
            }
        }
    }

    public ProductionJobFilterOption SelectedProductionJobFilter
    {
        get => selectedProductionJobFilter;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref selectedProductionJobFilter, value))
            {
                RefreshProductionJobPlan();
            }
        }
    }

    public decimal MaxManufacturingJobHours
    {
        get => maxManufacturingJobHours;
        set
        {
            if (SetProperty(ref maxManufacturingJobHours, Math.Max(0.01m, value)))
            {
                RefreshProductionJobPlan();
            }
        }
    }

    public decimal MaxReactionJobHours
    {
        get => maxReactionJobHours;
        set
        {
            if (SetProperty(ref maxReactionJobHours, Math.Max(0.01m, value)))
            {
                RefreshProductionJobPlan();
            }
        }
    }

    public int MaxParallelManufacturingJobs
    {
        get => maxParallelManufacturingJobs;
        set
        {
            if (SetProperty(ref maxParallelManufacturingJobs, Math.Max(1, value)))
            {
                RefreshProductionJobPlan();
            }
        }
    }

    public int MaxParallelReactionJobs
    {
        get => maxParallelReactionJobs;
        set
        {
            if (SetProperty(ref maxParallelReactionJobs, Math.Max(1, value)))
            {
                RefreshProductionJobPlan();
            }
        }
    }

    public string ProductionJobPlanSummaryText
    {
        get
        {
            var allRows = BuildProductionJobRows(applyFilter: false).ToList();
            if (allRows.Count == 0)
            {
                return "No planned jobs.";
            }

            var openRows = allRows.Where(row => !row.IsCompleted).ToList();
            var estimatedCalendar = EstimateWaveCalendarTime(openRows);
            var remainingJobTime = TimeSpan.FromTicks(openRows.Sum(row => row.Duration.Ticks));
            var waveCount = allRows.Select(row => row.Wave).DefaultIfEmpty(0).Max();
            var dependencyWarnings = BuildProductionDependencyWarnings(allRows).Count;
            var warningText = dependencyWarnings == 0 ? string.Empty : $" | {dependencyWarnings:N0} dependency warnings";

            return $"{openRows.Count:N0} open / {allRows.Count:N0} total jobs | {waveCount:N0} waves | remaining job time {FormatDuration(remainingJobTime)} | estimated calendar {FormatDuration(estimatedCalendar)}{warningText}";
        }
    }

    public string ProductionJobPlanWarningsText
    {
        get
        {
            var warnings = BuildProductionDependencyWarnings(BuildProductionJobRows(applyFilter: false).ToList());
            return warnings.Count == 0 ? string.Empty : string.Join(Environment.NewLine, warnings);
        }
    }

    private async Task SearchAsync()
    {
        IsBusy = true;
        StatusText = "Loading SDE blueprint data";

        try
        {
            var results = await blueprintRepository.SearchAsync(SearchText, CancellationToken.None);
            StatusText = "Preparing blueprint search results";
            Blueprints.Clear();
            foreach (var blueprint in results)
            {
                Blueprints.Add(blueprint);
            }

            SelectedBlueprint ??= Blueprints.FirstOrDefault();
            StatusText = $"{Blueprints.Count} blueprints loaded";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SearchSolarSystemsAsync()
    {
        var results = await solarSystemRepository.SearchSolarSystemsAsync(SolarSystemSearchText, CancellationToken.None);
        SolarSystemResults.Clear();
        foreach (var system in results)
        {
            SolarSystemResults.Add(new SolarSystemOption(
                system.SolarSystemId,
                system.Name,
                system.SecurityStatus,
                system.RegionId));
        }

        SelectedSolarSystem ??= SolarSystemResults.FirstOrDefault(system => system.SolarSystemId == FacilitySolarSystemId)
            ?? SolarSystemResults.FirstOrDefault();
    }

    private async Task RefreshFacilityCostIndexAsync()
    {
        if (FacilitySolarSystemId <= 0)
        {
            FacilityCostIndexStatus = "Select a system to load the ESI cost index.";
            return;
        }

        try
        {
            var costIndex = await industryCostIndexProvider.GetCostIndexAsync(
                FacilitySolarSystemId,
                SelectedFacilityService.Role,
                CancellationToken.None);

            if (costIndex is null)
            {
                FacilityCostIndexStatus = "No ESI cost index found for this system/activity.";
                return;
            }

            FacilitySystemCostIndex = costIndex.CostIndex;
            FacilityCostIndexStatus = $"ESI {SelectedFacilityService.Name} index as of {costIndex.AsOf.LocalDateTime:g}.";
            RefreshDataFreshness();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or JsonException or TaskCanceledException)
        {
            FacilityCostIndexStatus = "Could not refresh ESI cost index; manual value is kept.";
        }
    }

    private async Task CalculateAsync()
    {
        await CalculateAndApplyAsync(CancellationToken.None);
    }

    private async Task CalculateAndApplyAsync(CancellationToken cancellationToken)
    {
        if (SelectedBlueprint is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Calculating";

        try
        {
            Result = await calculator.CalculateAsync(CreateManufacturingRequest(SelectedBlueprint.BlueprintId), cancellationToken);

            Materials.Clear();
            foreach (var material in AggregateMaterialsForDisplay(Result.Materials))
            {
                Materials.Add(material);
            }

            shoppingList = shoppingListService.CreateFromManufacturingResult(Result);
            RefreshProductionTree(Result);
            RefreshDataFreshness();
            OnPropertyChanged(nameof(ShoppingListText));
            OnPropertyChanged(nameof(MaterialAuditSummaryText));
            StatusText = "Calculation complete";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void RefreshProductionTree(ManufacturingResult calculationResult)
    {
        var nodes = calculationResult.ProductionJobs
            .GroupBy(job => new
            {
                job.BlueprintId,
                job.ParentBlueprintId,
                job.ProductName,
                job.ActivityType,
                job.FacilityName,
                job.Depth,
                job.MaterialEfficiency,
                job.TimeEfficiency
            })
            .Select(group => new ProductionTreeNodeViewModel(
                group.Key.BlueprintId,
                group.Key.ParentBlueprintId,
                group.Key.ProductName,
                group.Key.ActivityType,
                group.Key.FacilityName,
                group.Sum(job => job.RequiredQuantity),
                group.Sum(job => job.TotalRuns),
                TimeSpan.FromTicks(group.Sum(job => job.TotalTime.Ticks)),
                group.Key.Depth,
                group.Key.MaterialEfficiency,
                group.Key.TimeEfficiency,
                ScheduleEfficiencyRecalculation))
            .OrderBy(node => node.Depth)
            .ThenBy(node => node.ProductName)
            .ToList();

        foreach (var node in nodes.Where(node => node.ParentBlueprintId is not null))
        {
            var parent = nodes.FirstOrDefault(candidate => candidate.BlueprintId == node.ParentBlueprintId);
            parent?.Children.Add(node);
        }

        ProductionTree.Clear();
        foreach (var root in nodes.Where(node => node.ParentBlueprintId is null))
        {
            ProductionTree.Add(root);
        }
    }

    private void ScheduleEfficiencyRecalculation(BlueprintId blueprintId, int materialEfficiency, int timeEfficiency)
    {
        blueprintEfficiencyOverrides[blueprintId] = new BlueprintEfficiencySettings(materialEfficiency, timeEfficiency);
        efficiencyRecalculation?.Cancel();
        efficiencyRecalculation?.Dispose();
        efficiencyRecalculation = new CancellationTokenSource();
        _ = RecalculateAfterEfficiencyChangeAsync(efficiencyRecalculation.Token);
    }

    private async Task RefreshSdeAsync()
    {
        IsBusy = true;
        try
        {
            var refreshed = await SdeDownloadService.EnsureAvailableAsync(
                new Progress<string>(message => StatusText = message),
                CancellationToken.None,
                forceRefresh: true);
            RefreshDataFreshness();
            StatusText = refreshed
                ? "SDE refreshed. Restart the application to load the new blueprint data."
                : "SDE refresh failed; the currently loaded data remains available.";
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void RefreshDataFreshness()
    {
        var snapshot = dataFreshnessService.GetSnapshot();
        SdeFreshnessText = snapshot.SdeText;
        MarketFreshnessText = snapshot.MarketText;
        CostIndexFreshnessText = snapshot.CostIndexText;
    }

    private async Task RecalculateAfterEfficiencyChangeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await CalculateAndApplyAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private ManufacturingRequest CreateManufacturingRequest(BlueprintId blueprintId)
    {
        return CreateManufacturingRequest(
            blueprintId,
            Runs,
            Lines,
            MaterialEfficiency,
            TimeEfficiency,
            AdditionalCosts,
            MaxManufacturingJobHours,
            MaxReactionJobHours);
    }

    private static IReadOnlyList<MaterialRequirement> AggregateMaterialsForDisplay(IEnumerable<MaterialRequirement> materials)
    {
        return materials
            .GroupBy(material => new { material.TypeId, material.Name })
            .Select(group =>
            {
                var groupMaterials = group.ToList();
                var quantity = groupMaterials.Sum(material => material.Quantity);
                var totalPrice = groupMaterials.Sum(material => material.TotalPrice);
                var hasEnoughMarketVolume = groupMaterials.All(material => material.HasEnoughMarketVolume);
                var missingPrice = groupMaterials.Any(material => material.MissingPrice);

                return new MaterialRequirement
                {
                    TypeId = group.Key.TypeId,
                    Name = group.Key.Name,
                    Quantity = quantity,
                    UnitPrice = quantity == 0 ? 0m : totalPrice / quantity,
                    TotalVolume = groupMaterials.Sum(material => material.TotalVolume),
                    Category = groupMaterials.First().Category,
                    MissingPrice = missingPrice,
                    HasEnoughMarketVolume = hasEnoughMarketVolume,
                    MarketFilledQuantity = groupMaterials.Sum(material => material.MarketFilledQuantity),
                    Calculation = groupMaterials.Count == 1 ? groupMaterials[0].Calculation : new MaterialCalculationBreakdown(),
                    CalculationOverrideText = groupMaterials.Count == 1
                        ? string.Empty
                        : $"{groupMaterials.Count:N0} job lines summed; total keeps per-job rounding"
                };
            })
            .OrderBy(material => material.Name)
            .ToList();
    }

    private ManufacturingRequest CreateManufacturingRequest(
        BlueprintId blueprintId,
        int requestRuns,
        int requestLines,
        int requestMaterialEfficiency,
        int requestTimeEfficiency,
        decimal requestAdditionalCosts,
        decimal requestMaxManufacturingJobHours = 0,
        decimal requestMaxReactionJobHours = 0)
    {
        return new ManufacturingRequest
        {
            BlueprintId = blueprintId,
            Runs = requestRuns,
            Lines = requestLines,
            MaterialEfficiency = requestMaterialEfficiency,
            TimeEfficiency = requestTimeEfficiency,
            DefaultComponentMaterialEfficiency = 10,
            DefaultComponentTimeEfficiency = 20,
            BlueprintEfficiencyOverrides = new Dictionary<BlueprintId, BlueprintEfficiencySettings>(blueprintEfficiencyOverrides),
            AdditionalCosts = requestAdditionalCosts,
            EnableBuildBuy = EnableBuildBuy,
            BuildBuyDepth = SelectedBuildBuyDepth.Depth,
            MaxBuildBuyDepth = MaxBuildBuyDepth,
            FinalProductFacility = SelectedFinalProductFacility.ToFacilityProfile(),
            ComponentFacility = SelectedComponentFacility.ToFacilityProfile(),
            ReactionFacility = SelectedReactionFacility.ToFacilityProfile(),
            PriceProfile = new PriceProfile
            {
                MaterialPriceSelection = SelectedMaterialPriceStrategy.Selection,
                ProductPriceSelection = SelectedProductPriceStrategy.Selection,
                MaterialMarketLocationId = SelectedMaterialMarket.LocationId,
                MaterialMarketLocationName = SelectedMaterialMarket.Name,
                ProductMarketLocationId = SelectedProductMarket.LocationId,
                ProductMarketLocationName = SelectedProductMarket.Name
            },
            MaxManufacturingJobHours = requestMaxManufacturingJobHours,
            MaxReactionJobHours = requestMaxReactionJobHours
        };
    }

    private async Task RunMarketScannerAsync()
    {
        if (blueprintCatalogProvider is null)
        {
            MarketScannerStatusText = "Blueprint catalog is not available.";
            return;
        }

        var cacheKey = BuildMarketScannerCacheKey();
        if (marketScannerCache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAt <= TimeSpan.FromMinutes(5))
        {
            ApplyMarketScannerResults(cached.Rows);
            MarketScannerStatusText = $"Loaded {cached.Rows.Count:N0} cached scanner rows from {cached.CreatedAt.LocalDateTime:g}.";
            MarketScannerProgressText = "Cache hit.";
            return;
        }

        var serverCache = await TryLoadServerMarketScannerCacheAsync(cacheKey);
        if (serverCache is not null)
        {
            marketScannerCache[cacheKey] = new MarketScannerCacheEntry(DateTimeOffset.UtcNow, serverCache);
            ApplyMarketScannerResults(serverCache);
            MarketScannerStatusText = $"Loaded {serverCache.Count:N0} scanner rows from server cache.";
            MarketScannerProgressText = "Server cache hit.";
            return;
        }

        IsBusy = true;
        IsMarketScannerBusy = true;
        StatusText = "Scanning market";
        MarketScannerStatusText = "Loading blueprint catalog.";
        MarketScannerProgressText = string.Empty;

        try
        {
            var catalog = await blueprintCatalogProvider.GetManufacturableBlueprintsAsync(CancellationToken.None);
            var candidates = catalog
                .Where(item => MatchesMarketScannerFilter(item, SelectedMarketScannerFilter.Filter))
                .Where(item => MatchesMarketScannerTechFilter(item, SelectedMarketScannerTechFilter.Filter))
                .OrderBy(item => item.ProductName)
                .ToList();

            if (MarketScannerMaxItems > 0)
            {
                candidates = candidates.Take(MarketScannerMaxItems).ToList();
            }

            var rows = new List<MarketScannerResultRow>();
            for (var index = 0; index < candidates.Count; index++)
            {
                var item = candidates[index];
                MarketScannerProgressText = $"{index + 1:N0} / {candidates.Count:N0}: {item.ProductName}";

                try
                {
                    var scanResult = await calculator.CalculateAsync(CreateManufacturingRequest(item.BlueprintId), CancellationToken.None);
                    rows.Add(new MarketScannerResultRow(
                        item.BlueprintId.Value,
                        item.ProductTypeId.Value,
                        item.ProductName,
                        item.BlueprintName,
                        ClassifyMarketScannerItem(item),
                        FormatMarketScannerTechMeta(item),
                        item.ActivityType == BlueprintActivityType.Reaction ? "Reaction" : "Manufacturing",
                        scanResult.OutputQuantity,
                        scanResult.MaterialCost,
                        scanResult.JobCost,
                        scanResult.TotalCost,
                        scanResult.EstimatedRevenue,
                        scanResult.Profit,
                        scanResult.ProfitPercent,
                        scanResult.IskPerHour,
                        scanResult.TotalProductionTime,
                        scanResult.Warnings.Count,
                        scanResult.Materials.Count(material => material.MissingPrice),
                        scanResult.Materials.Count(material => !material.HasEnoughMarketVolume)));
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException or HttpRequestException or JsonException or TaskCanceledException)
                {
                    rows.Add(new MarketScannerResultRow(
                        item.BlueprintId.Value,
                        item.ProductTypeId.Value,
                        item.ProductName,
                        item.BlueprintName,
                        ClassifyMarketScannerItem(item),
                        FormatMarketScannerTechMeta(item),
                        item.ActivityType == BlueprintActivityType.Reaction ? "Reaction" : "Manufacturing",
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        TimeSpan.Zero,
                        1,
                        0,
                        0,
                        ex.Message));
                }
            }

            var orderedRows = rows
                .OrderByDescending(row => row.Profit)
                .ThenByDescending(row => row.IskPerHour)
                .ToList();

            marketScannerCache[cacheKey] = new MarketScannerCacheEntry(DateTimeOffset.UtcNow, orderedRows);
            _ = SaveServerMarketScannerCacheAsync(cacheKey, orderedRows);
            ApplyMarketScannerResults(orderedRows);
            MarketScannerStatusText = $"Scanned {orderedRows.Count:N0} items. Results are cached for 5 minutes.";
            StatusText = "Market scan complete";
        }
        finally
        {
            IsMarketScannerBusy = false;
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void ApplyMarketScannerResults(IReadOnlyList<MarketScannerResultRow> rows)
    {
        MarketScannerResults.Clear();
        foreach (var row in rows)
        {
            MarketScannerResults.Add(row);
        }
    }

    private string BuildMarketScannerCacheKey()
    {
        var efficiencySignature = string.Join(
            ",",
            blueprintEfficiencyOverrides
                .OrderBy(entry => entry.Key.Value)
                .Select(entry => $"{entry.Key.Value}:{entry.Value.MaterialEfficiency}:{entry.Value.TimeEfficiency}"));

        return string.Join(
            "|",
            SelectedMarketScannerFilter.Filter,
            SelectedMarketScannerTechFilter.Filter,
            MarketScannerMaxItems,
            Runs,
            Lines,
            MaxManufacturingJobHours,
            MaxReactionJobHours,
            MaterialEfficiency,
            TimeEfficiency,
            AdditionalCosts,
            EnableBuildBuy,
            SelectedBuildBuyDepth.Depth,
            MaxBuildBuyDepth,
            SelectedMaterialMarket.LocationId,
            SelectedProductMarket.LocationId,
            SelectedMaterialPriceStrategy.Selection,
            SelectedProductPriceStrategy.Selection,
            SelectedFinalProductFacility.Id,
            SelectedComponentFacility.Id,
            SelectedReactionFacility.Id,
            efficiencySignature);
    }

    private static bool MatchesMarketScannerFilter(BlueprintCatalogItem item, MarketScannerFilter filter)
    {
        return filter switch
        {
            MarketScannerFilter.All => true,
            MarketScannerFilter.Reactions => item.ActivityType == BlueprintActivityType.Reaction,
            MarketScannerFilter.Components => item.ActivityType == BlueprintActivityType.Manufacturing && ClassifyMarketScannerItem(item) == "Component",
            MarketScannerFilter.Ships => item.ProductCategoryId == 6,
            MarketScannerFilter.Modules => item.ProductCategoryId == 7,
            MarketScannerFilter.DronesAndFighters => item.ProductCategoryId is 18 or 87,
            MarketScannerFilter.Charges => item.ProductCategoryId == 8,
            MarketScannerFilter.Structures => item.ProductCategoryId is 22 or 23 or 65,
            MarketScannerFilter.Other => ClassifyMarketScannerItem(item) == "Other",
            _ => true
        };
    }

    private static bool MatchesMarketScannerTechFilter(BlueprintCatalogItem item, MarketScannerTechFilter filter)
    {
        return filter switch
        {
            MarketScannerTechFilter.All => true,
            MarketScannerTechFilter.Tech1 => (item.MetaGroupId is null or 1) && item.TechLevel <= 1,
            MarketScannerTechFilter.Tech2 => item.MetaGroupId == 2 || item.TechLevel == 2,
            MarketScannerTechFilter.Tech3 => item.MetaGroupId == 14 || item.TechLevel == 3,
            MarketScannerTechFilter.Faction => item.MetaGroupId == 4,
            MarketScannerTechFilter.Storyline => item.MetaGroupId == 3,
            MarketScannerTechFilter.Officer => item.MetaGroupId == 5,
            MarketScannerTechFilter.Deadspace => item.MetaGroupId == 6,
            MarketScannerTechFilter.OtherMeta => item.MetaGroupId is not null and not (1 or 2 or 3 or 4 or 5 or 6 or 14),
            _ => true
        };
    }

    private static string ClassifyMarketScannerItem(BlueprintCatalogItem item)
    {
        if (item.ActivityType == BlueprintActivityType.Reaction)
        {
            return "Reaction";
        }

        if (item.ProductCategoryId == 6)
        {
            return "Ship";
        }

        if (item.ProductCategoryId == 7)
        {
            return "Module";
        }

        if (item.ProductCategoryId is 18 or 87)
        {
            return "Drone / Fighter";
        }

        if (item.ProductCategoryId == 8)
        {
            return "Charge";
        }

        if (item.ProductCategoryId is 22 or 23 or 65)
        {
            return "Structure";
        }

        if (item.ProductName.Contains("component", StringComparison.OrdinalIgnoreCase)
            || item.BlueprintName.Contains("component", StringComparison.OrdinalIgnoreCase))
        {
            return "Component";
        }

        return "Other";
    }

    private static string FormatMarketScannerTechMeta(BlueprintCatalogItem item)
    {
        return item.MetaGroupId switch
        {
            1 => "Tech I",
            2 => "Tech II",
            3 => "Storyline",
            4 => "Faction",
            5 => "Officer",
            6 => "Deadspace",
            14 => "Tech III",
            _ when !string.IsNullOrWhiteSpace(item.MetaGroupName) => item.MetaGroupName,
            _ => item.TechLevel switch
            {
                2 => "Tech II",
                3 => "Tech III",
                _ => "Tech I"
            }
        };
    }

    private void CopyShoppingList()
    {
        if (ShoppingListText.Length == 0)
        {
            return;
        }

        Clipboard.SetText(ShoppingListText);
        StatusText = "Shopping list copied";
    }

    private void AddToProductionLedger()
    {
        if (Result is null)
        {
            return;
        }

        if (SelectedProductionLedger is null && ProductionLedgerEntries.Count == 0)
        {
            ProductionLedgerName = $"Production Ledger {DateTime.Now:yyyy-MM-dd HH:mm}";
        }

        var entry = new ProductionLedgerEntry(
            Guid.NewGuid(),
            DateTimeOffset.Now,
            Result.Blueprint.BlueprintId.Value,
            Result.Blueprint.BlueprintName,
            Result.Blueprint.ProductName,
            Runs,
            Lines,
            MaterialEfficiency,
            TimeEfficiency,
            SelectedMaterialMarket.Name,
            SelectedProductMarket.Name,
            SelectedFinalProductFacility.Name,
            SelectedComponentFacility.Name,
            SelectedReactionFacility.Name,
            Result);

        ProductionLedgerEntries.Add(entry);
        SelectedProductionLedgerEntry = entry;
        RefreshProductionLedger();
        SelectedWorkspaceTab = 2;
        StatusText = $"Added to production ledger: {entry.ProductName}";
        ScheduleProductionLedgerAutoSave();
    }

    private void NewProductionLedger()
    {
        selectedProductionLedger = null;
        OnPropertyChanged(nameof(SelectedProductionLedger));
        ProductionLedgerName = $"Production Ledger {DateTime.Now:yyyy-MM-dd HH:mm}";
        ProductionLedgerEntries.Clear();
        productionLedgerAssetDeductions.Clear();
        productionLedgerSplitResults.Clear();
        productionJobCompletionStates.Clear();
        SelectedProductionLedgerEntry = null;
        RefreshProductionLedger();
        SelectedWorkspaceTab = 2;
        StatusText = "New production ledger started";
    }

    private void RemoveSelectedProductionEntry()
    {
        if (SelectedProductionLedgerEntry is null)
        {
            return;
        }

        var removed = SelectedProductionLedgerEntry;
        ProductionLedgerEntries.Remove(removed);
        RemoveProductionJobStatesForEntry(removed.Id);
        productionLedgerSplitResults.Remove(removed.Id);
        SelectedProductionLedgerEntry = ProductionLedgerEntries.FirstOrDefault();
        RefreshProductionLedger();
        StatusText = $"Removed from production ledger: {removed.ProductName}";
        ScheduleProductionLedgerAutoSave();
    }

    private void ClearProductionLedger()
    {
        ProductionLedgerEntries.Clear();
        productionLedgerAssetDeductions.Clear();
        productionLedgerSplitResults.Clear();
        productionJobCompletionStates.Clear();
        SelectedProductionLedgerEntry = null;
        RefreshProductionLedger();
        StatusText = "Production ledger cleared";
        ScheduleProductionLedgerAutoSave();
    }

    private void CopyProductionShoppingList()
    {
        if (ProductionLedgerShoppingListText.Length == 0)
        {
            return;
        }

        Clipboard.SetText(ProductionLedgerShoppingListText);
        StatusText = "Production ledger shopping list copied";
    }

    private void UseProductionAssets()
    {
        if (ProductionLedgerEntries.Count == 0)
        {
            StatusText = "Production ledger is empty";
            return;
        }

        if (CharacterAccounts.Count == 0)
        {
            StatusText = "Add at least one character before loading assets";
            return;
        }

        var neededLines = productionLedgerShoppingList.Lines.ToList();

        var dialog = new ProductionAssetsDialog(
            assetService,
            CharacterAccounts.Select(account => account.ToSaved()).ToList(),
            neededLines)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        productionLedgerAssetDeductions.Clear();
        foreach (var deduction in dialog.SelectedDeductions)
        {
            productionLedgerAssetDeductions[deduction.Key] = deduction.Value;
        }

        RefreshProductionLedger();
        StatusText = $"Applied {productionLedgerAssetDeductions.Values.Sum():N0} owned asset units to production ledger";
    }

    private async Task SaveProductionLedgerAsync()
    {
        try
        {
            var document = CreateProductionLedgerDocument();
            var ledgerId = await dataService.SaveProductionLedgerAsync(
                SelectedProductionLedger?.Id ?? string.Empty,
                ProductionLedgerName,
                document);
            UpsertProductionLedgerOption(new ProductionLedgerOption(ledgerId, ProductionLedgerName, document), select: true);
            StatusText = "Production ledger saved to server";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            StatusText = $"Could not save production ledger: {ex.Message}";
        }
    }

    private void ScheduleProductionLedgerAutoSave()
    {
        if (isApplyingServerData || SelectedCharacterAccount is null || ProductionLedgerEntries.Count == 0)
        {
            return;
        }

        productionLedgerAutoSave?.Cancel();
        productionLedgerAutoSave?.Dispose();
        productionLedgerAutoSave = new CancellationTokenSource();
        var token = productionLedgerAutoSave.Token;
        _ = AutoSaveProductionLedgerAsync(token);
    }

    private async Task AutoSaveProductionLedgerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken);
            if (cancellationToken.IsCancellationRequested || ProductionLedgerEntries.Count == 0 || SelectedCharacterAccount is null)
            {
                return;
            }

            var document = CreateProductionLedgerDocument();
            var ledgerId = await dataService.SaveProductionLedgerAsync(
                SelectedProductionLedger?.Id ?? string.Empty,
                ProductionLedgerName,
                document,
                cancellationToken);
            UpsertProductionLedgerOption(new ProductionLedgerOption(ledgerId, ProductionLedgerName, document), select: true);
            StatusText = $"Production ledger autosaved: {ProductionLedgerName}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            StatusText = $"Could not autosave production ledger: {ex.Message}";
        }
    }

    private async Task<IReadOnlyList<MarketScannerResultRow>?> TryLoadServerMarketScannerCacheAsync(string cacheKey)
    {
        if (SelectedCharacterAccount is null && CharacterAccounts.Count == 0)
        {
            return null;
        }

        try
        {
            var snapshot = await dataService.LoadSnapshotAsync();
            var cacheEntry = snapshot?.MarketScanCache.FirstOrDefault(entry =>
                entry.CacheKey == cacheKey && entry.ExpiresAt > DateTimeOffset.UtcNow);
            return cacheEntry?.Rows;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task SaveServerMarketScannerCacheAsync(string cacheKey, IReadOnlyList<MarketScannerResultRow> rows)
    {
        if (SelectedCharacterAccount is null && CharacterAccounts.Count == 0)
        {
            return;
        }

        try
        {
            await dataService.SaveMarketScanCacheAsync(cacheKey, DateTimeOffset.UtcNow.AddMinutes(5), rows);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            MarketScannerStatusText = $"Scan complete; server cache save failed: {ex.Message}";
        }
    }

    private async Task LoadProductionLedgerAsync()
    {
        try
        {
            var snapshot = await dataService.LoadSnapshotAsync();
            if (snapshot is null || snapshot.ProductionLedgers.Count == 0)
            {
                StatusText = "No production ledger saved on server";
                return;
            }

            LoadProductionLedgerOptions(snapshot.ProductionLedgers);
            SelectedProductionLedger = ProductionLedgers.FirstOrDefault();
            SelectedWorkspaceTab = 2;
            StatusText = $"Loaded {ProductionLedgers.Count:N0} production ledgers from server";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            StatusText = $"Could not load production ledger: {ex.Message}";
        }
    }

    private async Task DeleteProductionLedgerAsync()
    {
        if (SelectedProductionLedger is null)
        {
            return;
        }

        var deleted = SelectedProductionLedger;
        try
        {
            await dataService.DeleteProductionLedgerAsync(deleted.Id);
            ProductionLedgers.Remove(deleted);
            NewProductionLedger();
            StatusText = $"Production ledger deleted: {deleted.Name}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            StatusText = $"Could not delete production ledger: {ex.Message}";
        }
    }

    private ServerProductionLedgerDocument CreateProductionLedgerDocument()
    {
        return new ServerProductionLedgerDocument
        {
            SavedAt = DateTimeOffset.Now,
            Settings = new ProductionPlannerSettings
            {
                Scope = SelectedProductionPlannerScope.Scope,
                Filter = SelectedProductionJobFilter.Filter,
                MaxManufacturingJobHours = MaxManufacturingJobHours,
                MaxReactionJobHours = MaxReactionJobHours,
                MaxParallelManufacturingJobs = MaxParallelManufacturingJobs,
                MaxParallelReactionJobs = MaxParallelReactionJobs
            },
            Entries = ProductionLedgerEntries.ToList(),
            JobStates = productionJobCompletionStates.Values
                .Where(state => state.IsCompleted || !string.IsNullOrWhiteSpace(state.Notes))
                .ToList()
        };
    }

    private void ApplyProductionLedgerDocument(ServerProductionLedgerDocument document, bool replaceExisting)
    {
        if (replaceExisting)
        {
            ProductionLedgerEntries.Clear();
            productionLedgerAssetDeductions.Clear();
            productionLedgerSplitResults.Clear();
            productionJobCompletionStates.Clear();
        }

        SelectedProductionPlannerScope = ProductionPlannerScopes.FirstOrDefault(option => option.Scope == document.Settings.Scope)
            ?? ProductionPlannerScopes.First();
        SelectedProductionJobFilter = ProductionJobFilters.FirstOrDefault(option => option.Filter == document.Settings.Filter)
            ?? ProductionJobFilters.First();
        MaxManufacturingJobHours = document.Settings.MaxManufacturingJobHours;
        MaxReactionJobHours = document.Settings.MaxReactionJobHours;
        MaxParallelManufacturingJobs = document.Settings.MaxParallelManufacturingJobs;
        MaxParallelReactionJobs = document.Settings.MaxParallelReactionJobs;

        foreach (var state in document.JobStates.Where(state => !string.IsNullOrWhiteSpace(state.StableKey)))
        {
            productionJobCompletionStates[state.StableKey] = state;
        }

        foreach (var entry in document.Entries)
        {
            ProductionLedgerEntries.Add(entry);
        }

        SelectedProductionLedgerEntry = ProductionLedgerEntries.FirstOrDefault();
        RefreshProductionLedger();
    }

    private void LoadProductionLedgerOptions(IReadOnlyList<ServerProductionLedgerRecord> records)
    {
        ProductionLedgers.Clear();
        foreach (var record in records.OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase))
        {
            ProductionLedgers.Add(new ProductionLedgerOption(record.Id, record.Name, record.Ledger));
        }
    }

    private void UpsertProductionLedgerOption(ProductionLedgerOption option, bool select)
    {
        var existing = ProductionLedgers.FirstOrDefault(ledger => ledger.Id == option.Id);
        if (existing is null)
        {
            ProductionLedgers.Add(option);
        }
        else
        {
            var index = ProductionLedgers.IndexOf(existing);
            ProductionLedgers[index] = option;
        }

        if (select)
        {
            SelectedProductionLedger = option;
        }
    }

    private void RefreshProductionLedger()
    {
        var baseShoppingList = shoppingListService.CreateFromManufacturingResults(
            ProductionLedgerEntries.SelectMany(GetProductionLedgerResults));
        productionLedgerShoppingList = ApplyProductionLedgerAssetDeductions(baseShoppingList);

        ProductionLedgerMaterials.Clear();
        foreach (var line in productionLedgerShoppingList.Lines)
        {
            ProductionLedgerMaterials.Add(line);
        }

        OnPropertyChanged(nameof(ProductionLedgerShoppingListText));
        OnPropertyChanged(nameof(ProductionLedgerSummaryText));
        OnPropertyChanged(nameof(SelectedProductionTreeText));
        OnPropertyChanged(nameof(SelectedProductionBreakdownText));
        RefreshProductionJobPlan();
        RaiseProductionLedgerCommandStates();
    }

    private ShoppingList ApplyProductionLedgerAssetDeductions(ShoppingList baseShoppingList)
    {
        if (productionLedgerAssetDeductions.Count == 0)
        {
            return baseShoppingList;
        }

        var lines = baseShoppingList.Lines
            .Select(line =>
            {
                var deduction = productionLedgerAssetDeductions.TryGetValue(line.TypeId, out var quantity)
                    ? Math.Min(quantity, line.Quantity)
                    : 0;

                var adjustedQuantity = Math.Max(0, line.Quantity - deduction);
                if (adjustedQuantity == line.Quantity)
                {
                    return line;
                }

                var unitCost = line.Quantity == 0 ? 0 : line.EstimatedCost / line.Quantity;
                return new ShoppingListLine
                {
                    TypeId = line.TypeId,
                    Name = line.Name,
                    Quantity = adjustedQuantity,
                    EstimatedCost = unitCost * adjustedQuantity
                };
            })
            .Where(line => line.Quantity > 0)
            .ToList();

        return new ShoppingList { Lines = lines };
    }

    private void RemoveProductionJobStatesForEntry(Guid entryId)
    {
        var prefix = $"{entryId:N}:";
        foreach (var key in productionJobCompletionStates.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            productionJobCompletionStates.Remove(key);
        }
    }

    private void RefreshProductionJobPlan()
    {
        var rows = BuildProductionJobRows(applyFilter: true).ToList();
        ProductionJobPlanRows.Clear();
        foreach (var row in rows)
        {
            ProductionJobPlanRows.Add(row);
        }

        OnPropertyChanged(nameof(ProductionJobPlanSummaryText));
        OnPropertyChanged(nameof(ProductionJobPlanWarningsText));
    }

    private IEnumerable<PlannedProductionJobRow> BuildProductionJobRows(bool applyFilter)
    {
        var rows = new List<PlannedProductionJobRow>();
        var sourceEntries = SelectedProductionPlannerScope.Scope == ProductionPlannerScope.SelectedLedgerItem
            ? ProductionLedgerEntries.Where(entry => SelectedProductionLedgerEntry?.Id == entry.Id)
            : ProductionLedgerEntries;
        var requirements = sourceEntries
            .SelectMany(entry =>
            {
                var hasSplitResults = productionLedgerSplitResults.ContainsKey(entry.Id);
                return GetProductionLedgerResults(entry).SelectMany((result, resultIndex) =>
                {
                    var jobs = result.ProductionJobs.ToList();
                    var maxDepth = jobs.Select(job => job.Depth).DefaultIfEmpty(0).Max();
                    return jobs.Select((job, jobIndex) => new ProductionJobRequirementSource(
                        entry.Id,
                        entry.ProductName,
                        job,
                        maxDepth - job.Depth + 1,
                        hasSplitResults ? resultIndex : -1,
                        jobIndex));
                });
            })
            .Select(source => new AggregatedProductionJobRequirement(
                source.SourceProductName,
                source.Requirement.ParentProductName,
                source.Requirement.BlueprintId,
                source.Requirement.BlueprintName,
                source.Requirement.ProductTypeId,
                source.Requirement.ProductName,
                source.Requirement.ActivityType,
                source.Requirement.FacilityName,
                source.Requirement.RequiredQuantity,
                source.Requirement.OutputQuantityPerRun,
                source.Requirement.TotalRuns,
                source.Requirement.TimePerRun,
                source.DependencyStage,
                source.Requirement.Depth,
                source.SplitGroupIndex,
                source.JobIndex))
            .OrderBy(requirement => requirement.DependencyStage)
            .ThenBy(requirement => requirement.ProductName)
            .ToList();

        var expandedRequirements = requirements
            .SelectMany(requirement =>
            {
                var runsPerJob = requirement.ParentProductName.Length == 0
                    ? requirement.TotalRuns
                    : CalculateRunsPerJob(
                        requirement.TotalRuns,
                        requirement.TimePerRun,
                        TimeSpan.FromHours((double)(requirement.ActivityType == BlueprintActivityType.Reaction
                            ? MaxReactionJobHours
                            : MaxManufacturingJobHours)));
                var jobCount = (int)Math.Ceiling(requirement.TotalRuns / (decimal)runsPerJob);

                return Enumerable.Range(0, jobCount)
                    .Select(index =>
                    {
                        var runs = Math.Min(runsPerJob, requirement.TotalRuns - index * runsPerJob);
                        return new ExpandedProductionJobRequirement(
                            requirement,
                            runs,
                            TimeSpan.FromTicks(requirement.TimePerRun.Ticks * runs),
                            index);
                    });
            })
            .ToList();

        var jobCounters = new Dictionary<string, int>(StringComparer.Ordinal);
        var jobCounts = expandedRequirements
            .GroupBy(expanded => GetProductionJobGroupKey(expanded.Requirement), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var expanded in expandedRequirements)
        {
            var requirement = expanded.Requirement;
            var groupKey = GetProductionJobGroupKey(requirement);
            jobCounters.TryGetValue(groupKey, out var currentIndex);
            currentIndex++;
            jobCounters[groupKey] = currentIndex;

            var stableKey = $"{requirement.BlueprintId.Value}:{requirement.ActivityType}:{requirement.FacilityName}:{requirement.DependencyStage}:{requirement.SplitGroupIndex}:{requirement.JobIndex}:{expanded.SplitIndex}";
            productionJobCompletionStates.TryGetValue(stableKey, out var state);
            var row = new PlannedProductionJobRow(
                stableKey,
                requirement.SourceProductName,
                requirement.ProductName,
                requirement.BlueprintName,
                requirement.ActivityType,
                requirement.FacilityName,
                requirement.ParentProductName,
                requirement.RequiredQuantity,
                requirement.OutputQuantityPerRun,
                requirement.TotalRuns,
                requirement.DependencyStage,
                currentIndex,
                jobCounts[groupKey],
                expanded.Runs,
                requirement.TimePerRun,
                expanded.Duration,
                requirement.Depth,
                OnProductionJobCompletionChanged);
            row.ApplyState(state?.IsCompleted ?? false, state?.CompletedAt, state?.Notes ?? string.Empty);

            rows.Add(row);
        }

        AssignExecutionWaves(rows);

        return applyFilter
            ? rows.Where(JobMatchesFilter)
            : rows;
    }

    private static string GetProductionJobGroupKey(AggregatedProductionJobRequirement requirement)
    {
        return $"{requirement.SourceProductName}|{requirement.BlueprintId.Value}|{requirement.ActivityType}|{requirement.FacilityName}|{requirement.DependencyStage}|{requirement.ProductName}";
    }

    private static int CalculateRunsPerJob(int totalRuns, TimeSpan timePerRun, TimeSpan maxJobDuration)
    {
        if (totalRuns <= 1 || timePerRun <= TimeSpan.Zero)
        {
            return Math.Max(1, totalRuns);
        }

        var runsByDuration = (int)Math.Floor(maxJobDuration.TotalSeconds / timePerRun.TotalSeconds);
        return Math.Clamp(runsByDuration, 1, totalRuns);
    }

    private void AssignExecutionWaves(IReadOnlyList<PlannedProductionJobRow> rows)
    {
        var nextWave = 1;
        foreach (var stageGroup in rows.GroupBy(row => row.DependencyStage).OrderBy(group => group.Key))
        {
            var maxStageBatches = 1;
            foreach (var activityGroup in stageGroup.GroupBy(row => row.ActivityType))
            {
                var parallelLimit = activityGroup.Key == BlueprintActivityType.Reaction
                    ? MaxParallelReactionJobs
                    : MaxParallelManufacturingJobs;
                var orderedRows = activityGroup
                    .OrderBy(row => row.SourceProductName)
                    .ThenBy(row => row.ProductName)
                    .ThenBy(row => row.JobIndex)
                    .ToList();

                for (var index = 0; index < orderedRows.Count; index++)
                {
                    var batch = index / Math.Max(1, parallelLimit);
                    orderedRows[index].SetWave(nextWave + batch);
                    maxStageBatches = Math.Max(maxStageBatches, batch + 1);
                }
            }

            nextWave += maxStageBatches;
        }
    }

    private void OnProductionJobCompletionChanged(PlannedProductionJobRow row)
    {
        productionJobCompletionStates[row.StableKey] = new ProductionJobCompletionState(
            row.StableKey,
            row.IsCompleted,
            row.CompletedAt,
            row.Notes);
        OnPropertyChanged(nameof(ProductionJobPlanSummaryText));
        OnPropertyChanged(nameof(ProductionJobPlanWarningsText));
        ScheduleProductionLedgerAutoSave();
    }

    private bool JobMatchesFilter(PlannedProductionJobRow row)
    {
        return SelectedProductionJobFilter.Filter switch
        {
            ProductionJobFilter.Open => !row.IsCompleted,
            ProductionJobFilter.Completed => row.IsCompleted,
            _ => true
        };
    }

    private IReadOnlyList<ManufacturingResult> GetProductionLedgerResults(ProductionLedgerEntry entry)
    {
        return productionLedgerSplitResults.TryGetValue(entry.Id, out var splitResults)
            ? splitResults
            : [entry.Result];
    }

    private async Task RecalculateProductionLedgerPlanningAsync()
    {
        if (ProductionLedgerEntries.Count == 0)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Recalculating production ledger";

        try
        {
            productionLedgerSplitResults.Clear();
            foreach (var entry in ProductionLedgerEntries)
            {
                var splitResults = new List<ManufacturingResult>();
                for (var lineIndex = 0; lineIndex < entry.Lines; lineIndex++)
                {
                    var remainingRuns = entry.Runs;
                    while (remainingRuns > 0)
                    {
                        var chunkRuns = remainingRuns;
                        var chunkAdditionalCosts = entry.Result.AdditionalCosts * chunkRuns / (entry.Runs * entry.Lines);
                        splitResults.Add(await calculator.CalculateAsync(
                            CreateManufacturingRequest(
                                new BlueprintId(entry.BlueprintId),
                                chunkRuns,
                                1,
                                entry.MaterialEfficiency,
                                entry.TimeEfficiency,
                                chunkAdditionalCosts,
                                MaxManufacturingJobHours,
                                MaxReactionJobHours),
                            CancellationToken.None));
                        remainingRuns -= chunkRuns;
                    }
                }

                if (splitResults.Count > 0)
                {
                    productionLedgerSplitResults[entry.Id] = splitResults;
                }
            }

            RefreshProductionLedger();
            ScheduleProductionLedgerAutoSave();
            StatusText = "Production ledger recalculated";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not recalculate production ledger: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static TimeSpan EstimateCalendarTime(IEnumerable<PlannedProductionJobRow> rows, int parallelJobs)
    {
        var durations = rows
            .Select(row => row.Duration)
            .OrderByDescending(duration => duration)
            .ToList();
        if (durations.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var lanes = Enumerable.Repeat(TimeSpan.Zero, Math.Max(1, parallelJobs)).ToArray();
        foreach (var duration in durations)
        {
            var laneIndex = Array.IndexOf(lanes, lanes.Min());
            lanes[laneIndex] += duration;
        }

        return lanes.Max();
    }

    private TimeSpan EstimateWaveCalendarTime(IEnumerable<PlannedProductionJobRow> rows)
    {
        var total = TimeSpan.Zero;
        foreach (var waveGroup in rows.GroupBy(row => row.Wave).OrderBy(group => group.Key))
        {
            var manufacturingCalendar = EstimateCalendarTime(
                waveGroup.Where(row => row.ActivityType == BlueprintActivityType.Manufacturing),
                MaxParallelManufacturingJobs);
            var reactionCalendar = EstimateCalendarTime(
                waveGroup.Where(row => row.ActivityType == BlueprintActivityType.Reaction),
                MaxParallelReactionJobs);
            total += manufacturingCalendar > reactionCalendar ? manufacturingCalendar : reactionCalendar;
        }

        return total;
    }

    private static IReadOnlyList<string> BuildProductionDependencyWarnings(IReadOnlyList<PlannedProductionJobRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var parentRows = rows
            .Where(row => row.ParentProductName.Length == 0)
            .GroupBy(row => (row.SourceProductName, row.ProductName))
            .ToDictionary(group => group.Key, group => group.Min(row => row.Wave));
        var warnings = new List<string>();

        foreach (var componentGroup in rows
                     .Where(row => row.ParentProductName.Length > 0)
                     .GroupBy(row => (row.SourceProductName, row.ParentProductName, row.ProductName)))
        {
            if (!parentRows.TryGetValue((componentGroup.Key.SourceProductName, componentGroup.Key.ParentProductName), out var parentWave))
            {
                continue;
            }

            var latestComponentWave = componentGroup.Max(row => row.Wave);
            if (latestComponentWave >= parentWave)
            {
                warnings.Add($"{componentGroup.Key.ProductName} for {componentGroup.Key.ParentProductName} finishes in wave {latestComponentWave:N0}, parent starts in wave {parentWave:N0}.");
            }
        }

        return warnings;
    }

    private void RaiseCommandStates()
    {
        if (CalculateCommand is AsyncRelayCommand calculateCommand)
        {
            calculateCommand.RaiseCanExecuteChanged();
        }

        if (CopyShoppingListCommand is RelayCommand copyCommand)
        {
            copyCommand.RaiseCanExecuteChanged();
        }

        if (RunMarketScannerCommand is AsyncRelayCommand scannerCommand)
        {
            scannerCommand.RaiseCanExecuteChanged();
        }

        if (RefreshSdeCommand is AsyncRelayCommand refreshSdeCommand)
        {
            refreshSdeCommand.RaiseCanExecuteChanged();
        }

        RaiseProductionLedgerCommandStates();
        RaiseFacilityEditCommandStates();
        RaiseCharacterCommandStates();
    }

    private void RaiseProductionLedgerCommandStates()
    {
        if (AddToProductionCommand is RelayCommand addCommand)
        {
            addCommand.RaiseCanExecuteChanged();
        }

        if (RemoveProductionEntryCommand is RelayCommand removeCommand)
        {
            removeCommand.RaiseCanExecuteChanged();
        }

        if (ClearProductionLedgerCommand is RelayCommand clearCommand)
        {
            clearCommand.RaiseCanExecuteChanged();
        }

        if (CopyProductionShoppingListCommand is RelayCommand copyCommand)
        {
            copyCommand.RaiseCanExecuteChanged();
        }

        if (UseProductionAssetsCommand is RelayCommand useAssetsCommand)
        {
            useAssetsCommand.RaiseCanExecuteChanged();
        }

        if (SaveProductionLedgerCommand is AsyncRelayCommand saveCommand)
        {
            saveCommand.RaiseCanExecuteChanged();
        }

        if (LoadProductionLedgerCommand is AsyncRelayCommand loadCommand)
        {
            loadCommand.RaiseCanExecuteChanged();
        }

        if (DeleteProductionLedgerCommand is AsyncRelayCommand deleteLedgerCommand)
        {
            deleteLedgerCommand.RaiseCanExecuteChanged();
        }

        if (RecalculateProductionPlanCommand is AsyncRelayCommand recalculateCommand)
        {
            recalculateCommand.RaiseCanExecuteChanged();
        }
    }

    private void RaiseFacilityEditCommandStates()
    {
        if (DeleteFacilityCommand is RelayCommand deleteCommand)
        {
            deleteCommand.RaiseCanExecuteChanged();
        }
    }

    private void RaiseCharacterCommandStates()
    {
        if (AddCharacterCommand is AsyncRelayCommand addCommand)
        {
            addCommand.RaiseCanExecuteChanged();
        }

        if (DeleteCharacterCommand is RelayCommand deleteCommand)
        {
            deleteCommand.RaiseCanExecuteChanged();
        }
    }

    private static string BuildProductionTreeText(ManufacturingResult result)
    {
        var lines = new List<string>
        {
            $"{result.OutputQuantity:N0} x {result.Blueprint.ProductName}",
            $"  Final blueprint: {result.Blueprint.BlueprintName}"
        };

        if (result.BuildBuyDecisions.Count > 0)
        {
            lines.Add("  Build / buy plan:");
            foreach (var decision in result.BuildBuyDecisions.OrderBy(decision => decision.Depth).ThenBy(decision => decision.Name))
            {
                var indent = new string(' ', Math.Clamp(decision.Depth, 0, 10) * 2 + 4);
                var action = decision.Built ? "BUILD" : "BUY";
                var facility = string.IsNullOrWhiteSpace(decision.FacilityName) ? string.Empty : $" @ {decision.FacilityName}";
                lines.Add($"{indent}{action} {decision.RequiredQuantity:N0} x {decision.Name}{facility}");
                lines.Add($"{indent}  buy {decision.BuyCost:N2} ISK / build {decision.BuildCost:N2} ISK");
            }
        }
        else
        {
            lines.Add("  Direct material purchase only.");
        }

        lines.Add("  Final shopping materials:");
        foreach (var material in result.Materials.OrderBy(material => material.Name))
        {
            lines.Add($"    {material.Quantity:N0} x {material.Name}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildProductionBreakdownText(ProductionLedgerEntry entry)
    {
        var result = entry.Result;
        return string.Join(
            Environment.NewLine,
            [
                $"{entry.OutputText}",
                $"Added: {entry.AddedAt.LocalDateTime:g}",
                $"Runs/job: {entry.Runs:N0} / Lines: {entry.Lines:N0} / ME {entry.MaterialEfficiency} / TE {entry.TimeEfficiency}",
                $"Markets: buy {entry.MaterialMarketName} / sell {entry.ProductMarketName}",
                $"Facilities: final {entry.FinalProductFacilityName} / components {entry.ComponentFacilityName} / reactions {entry.ReactionFacilityName}",
                string.Empty,
                $"Material cost: {FormatIsk(result.MaterialCost)}",
                $"Job cost: {FormatIsk(result.JobCost)}",
                $"Additional costs: {FormatIsk(result.AdditionalCosts)}",
                $"Total cost: {FormatIsk(result.TotalCost)}",
                $"Revenue: {FormatIsk(result.EstimatedRevenue)}",
                $"Profit: {FormatIsk(result.Profit)}",
                $"ISK / hour: {FormatIsk(result.IskPerHour)}",
                string.Empty,
                $"Final time: {FormatDuration(result.FinalProductionTime)}",
                $"Build time: {FormatDuration(result.BuildProductionTime)}",
                $"Total time: {FormatDuration(result.TotalProductionTime)}"
            ]);
    }

    private static string FormatIsk(decimal? value)
    {
        return value is null ? "-" : $"{value.Value:N2} ISK";
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalDays >= 1)
        {
            return $"{(int)value.TotalDays:N0}d {value.Hours:D2}h {value.Minutes:D2}m";
        }

        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours:N0}h {value.Minutes:D2}m";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(value.TotalMinutes)):N0}m";
    }

    private static string FormatFacilityName(string facilityName)
    {
        return string.IsNullOrWhiteSpace(facilityName) ? string.Empty : $" @ {facilityName}";
    }

    private void RecalculateFacilityMultipliers()
    {
        var result = FacilityFittingCalculator.Calculate(
            SelectedFacilityStructure.Id,
            SelectedFacilitySecurity.SecurityBand,
            SelectedFacilityRigIds());

        FacilityMaterialMultiplier = result.MaterialMultiplier;
        FacilityTimeMultiplier = result.TimeMultiplier;
        FacilityJobCostMultiplier = result.JobCostMultiplier;
        OnPropertyChanged(nameof(FacilityMultiplierSummary));
    }

    private void SaveFacility()
    {
        var rigIds = SelectedFacilityRigIds().ToList();
        var serviceModuleIds = SelectedServiceModuleIds().ToList();
        var profileId = SelectedEditableFacility is not null
            && SelectedEditableFacility.Id != SavedFacilityProfile.NoneId
            ? SelectedEditableFacility.Id
            : Guid.NewGuid();
        var profile = new FacilityOption(
            profileId,
            string.IsNullOrWhiteSpace(FacilityName) ? "Unnamed Facility" : FacilityName.Trim(),
            FacilitySolarSystemId,
            string.IsNullOrWhiteSpace(FacilitySolarSystemName) ? "Unspecified" : FacilitySolarSystemName.Trim(),
            SelectedFacilityStructure.Id,
            SelectedFacilityStructure.Name,
            SelectedFacilityService.Role,
            serviceModuleIds,
            SelectedFacilitySecurity.SecurityBand,
            rigIds,
            FacilityMaterialMultiplier,
            FacilityTimeMultiplier,
            FacilityJobCostMultiplier,
            FacilitySystemCostIndex,
            FacilityTaxRate);

        var existingIndex = FacilityProfiles
            .Select((facility, index) => new { facility, index })
            .FirstOrDefault(entry => entry.facility.Id == profile.Id)?.index;

        var isNewProfile = existingIndex is null;
        if (isNewProfile)
        {
            FacilityProfiles.Add(profile);
        }
        else if (existingIndex is int index)
        {
            FacilityProfiles[index] = profile;
        }

        ReplaceAssignments(profile);
        SelectedEditableFacility = profile;
        if (isNewProfile)
        {
            SelectedFinalProductFacility = profile;
            SelectedComponentFacility = profile;
        }

        SaveUserSettings();
        StatusText = $"Facility saved: {profile.Name}";
    }

    private void NewFacility()
    {
        SelectedEditableFacility = null;
        FacilityName = "New Facility";
        FacilitySolarSystemName = "Jita";
        FacilitySolarSystemId = 30000142;
        SolarSystemSearchText = "Jita";
        SelectedFacilityStructure = FacilityStructures.First(option => option.Id == "raitaru");
        SelectedFacilityService = CostIndexActivities.First(option => option.Role == FacilityServiceRole.Manufacturing);
        SelectedFacilitySecurity = FacilitySecurityBands.First(option => option.SecurityBand == FacilitySecurityBand.HighSec);
        SetServiceSelections(["35878"]);
        SetRigSelections([]);
        FacilitySystemCostIndex = 0.04m;
        FacilityTaxRate = 0m;
        FacilityCostIndexStatus = "New facility.";
        RecalculateFacilityMultipliers();
        OnPropertyChanged(nameof(CanDeleteSelectedFacility));
        RaiseFacilityEditCommandStates();
    }

    private void DeleteFacility()
    {
        if (!CanDeleteSelectedFacility || SelectedEditableFacility is null)
        {
            return;
        }

        var deleted = SelectedEditableFacility;
        var fallback = FacilityProfiles.First(profile => profile.Id == SavedFacilityProfile.NoneId);
        FacilityProfiles.Remove(deleted);

        if (SelectedFinalProductFacility.Id == deleted.Id)
        {
            SelectedFinalProductFacility = fallback;
        }

        if (SelectedComponentFacility.Id == deleted.Id)
        {
            SelectedComponentFacility = fallback;
        }

        if (SelectedReactionFacility.Id == deleted.Id)
        {
            SelectedReactionFacility = fallback;
        }

        SaveUserSettings();
        NewFacility();
        StatusText = $"Facility deleted: {deleted.Name}";
    }

    private void ReplaceAssignments(FacilityOption profile)
    {
        if (SelectedFinalProductFacility.Id == profile.Id)
        {
            selectedFinalProductFacility = profile;
            OnPropertyChanged(nameof(SelectedFinalProductFacility));
            OnPropertyChanged(nameof(SelectedFinalProductFacilityText));
        }

        if (SelectedComponentFacility.Id == profile.Id)
        {
            selectedComponentFacility = profile;
            OnPropertyChanged(nameof(SelectedComponentFacility));
            OnPropertyChanged(nameof(SelectedComponentFacilityText));
        }

        if (SelectedReactionFacility.Id == profile.Id)
        {
            selectedReactionFacility = profile;
            OnPropertyChanged(nameof(SelectedReactionFacility));
            OnPropertyChanged(nameof(SelectedReactionFacilityText));
        }
    }

    private void LoadFacilityIntoEditor(FacilityOption profile)
    {
        FacilityName = profile.Id == SavedFacilityProfile.NoneId ? "New Facility" : profile.Name;
        FacilitySolarSystemName = profile.SolarSystemName;
        FacilitySolarSystemId = profile.SolarSystemId;
        SolarSystemSearchText = profile.SolarSystemName;
        SelectedFacilityStructure = FacilityStructures.FirstOrDefault(option => option.Id == profile.StructureId)
            ?? FacilityStructures.First(option => option.Id == "raitaru");
        SelectedFacilityService = CostIndexActivities.FirstOrDefault(option => option.Role == profile.ServiceRole)
            ?? CostIndexActivities.First(option => option.Role == FacilityServiceRole.Manufacturing);
        SelectedFacilitySecurity = FacilitySecurityBands.FirstOrDefault(option => option.SecurityBand == profile.SecurityBand)
            ?? FacilitySecurityBands.First(option => option.SecurityBand == FacilitySecurityBand.HighSec);
        SetServiceSelections(profile.ServiceModuleIds);
        SetRigSelections(profile.RigIds);
        FacilitySystemCostIndex = profile.SystemCostIndex;
        FacilityTaxRate = profile.FacilityTaxRate;
        FacilityCostIndexStatus = profile.Id == SavedFacilityProfile.NoneId
            ? "Select a saved facility or create a new one."
            : $"Editing {profile.Name}.";
        RecalculateFacilityMultipliers();
    }

    private void SetServiceSelections(IReadOnlyList<string> serviceModuleIds)
    {
        HasManufacturingPlant = serviceModuleIds.Contains("35878");
        HasSupercapitalShipyard = serviceModuleIds.Contains("35877");
        HasCapitalShipyard = serviceModuleIds.Contains("35881");
        HasCompositeReactor = serviceModuleIds.Contains("45537");
        HasHybridReactor = serviceModuleIds.Contains("45538");
        HasBiochemicalReactor = serviceModuleIds.Contains("45539");
        HasInventionLab = serviceModuleIds.Contains("35886");
        HasResearchLab = serviceModuleIds.Contains("35891");
        HasHyasyodaResearchLab = serviceModuleIds.Contains("45550");
        HasReprocessingFacility = serviceModuleIds.Contains("35899");
    }

    private void SetRigSelections(IReadOnlyList<string> rigIds)
    {
        SelectedFacilityRigSlot1 = IsFacilityRigSlot1Enabled
            ? FindRigOption(rigIds.ElementAtOrDefault(0))
            : EmptyRigOption;
        SelectedFacilityRigSlot2 = IsFacilityRigSlot2Enabled
            ? FindRigOption(rigIds.ElementAtOrDefault(1))
            : EmptyRigOption;
        SelectedFacilityRigSlot3 = IsFacilityRigSlot3Enabled
            ? FindRigOption(rigIds.ElementAtOrDefault(2))
            : EmptyRigOption;
    }

    private FacilityRigOption FindRigOption(string? rigId)
    {
        return FacilityRigs.FirstOrDefault(option => option.Id == rigId)
            ?? FacilityRigs.First(option => option.Id == FacilityFittingCalculator.NoneRigId);
    }

    private IEnumerable<string> SelectedFacilityRigIds()
    {
        yield return IsFacilityRigSlot1Enabled ? SelectedFacilityRigSlot1.Id : FacilityFittingCalculator.NoneRigId;
        yield return IsFacilityRigSlot2Enabled ? SelectedFacilityRigSlot2.Id : FacilityFittingCalculator.NoneRigId;
        yield return IsFacilityRigSlot3Enabled ? SelectedFacilityRigSlot3.Id : FacilityFittingCalculator.NoneRigId;
    }

    private IEnumerable<string> SelectedServiceModuleIds()
    {
        if (HasManufacturingPlant)
        {
            yield return "35878";
        }

        if (HasSupercapitalShipyard)
        {
            yield return "35877";
        }

        if (HasCapitalShipyard)
        {
            yield return "35881";
        }

        if (HasCompositeReactor)
        {
            yield return "45537";
        }

        if (HasHybridReactor)
        {
            yield return "45538";
        }

        if (HasBiochemicalReactor)
        {
            yield return "45539";
        }

        if (HasInventionLab)
        {
            yield return "35886";
        }

        if (HasResearchLab)
        {
            yield return "35891";
        }

        if (HasHyasyodaResearchLab)
        {
            yield return "45550";
        }

        if (HasReprocessingFacility)
        {
            yield return "35899";
        }
    }

    private void LoadFacilityRigOptions(IReadOnlyList<FacilityRigPreset> presets)
    {
        allFacilityRigs.Clear();
        allFacilityRigs.AddRange(presets.Select(preset => new FacilityRigOption(preset.Id, preset.Name, preset.RigSize, preset.Role, preset.ScopeName)));

        if (!allFacilityRigs.Any(option => option.Id == FacilityFittingCalculator.NoneRigId))
        {
            allFacilityRigs.Insert(0, EmptyRigOption);
        }

        RefreshFacilityRigOptions();
    }

    private void RefreshFacilityRigOptions()
    {
        var selectedRigIds = new[]
        {
            selectedFacilityRigSlot1.Id,
            selectedFacilityRigSlot2.Id,
            selectedFacilityRigSlot3.Id
        };

        FacilityRigs.Clear();
        foreach (var option in allFacilityRigs.Where(RigFitsSelectedStructure))
        {
            FacilityRigs.Add(option);
        }

        if (!FacilityRigs.Any(option => option.Id == FacilityFittingCalculator.NoneRigId))
        {
            FacilityRigs.Insert(0, EmptyRigOption);
        }

        selectedFacilityRigSlot1 = IsFacilityRigSlot1Enabled ? FindRigOption(selectedRigIds[0]) : EmptyRigOption;
        selectedFacilityRigSlot2 = IsFacilityRigSlot2Enabled ? FindRigOption(selectedRigIds[1]) : EmptyRigOption;
        selectedFacilityRigSlot3 = IsFacilityRigSlot3Enabled ? FindRigOption(selectedRigIds[2]) : EmptyRigOption;
        OnPropertyChanged(nameof(SelectedFacilityRigSlot1));
        OnPropertyChanged(nameof(SelectedFacilityRigSlot2));
        OnPropertyChanged(nameof(SelectedFacilityRigSlot3));
    }

    private bool RigFitsSelectedStructure(FacilityRigOption option)
    {
        return option.Id == FacilityFittingCalculator.NoneRigId
            || option.RigSize == FacilityRigSize.None
            || (SelectedFacilityStructure.RigSlots > 0 && option.RigSize == SelectedFacilityStructure.RigSize);
    }

    private static DefaultServices CreateDefaultServices()
    {
        var userSettingsService = new UserSettingsService(GetSettingsFilePath());
        var sdeDirectory = FindSdeDirectory();
        if (sdeDirectory is not null)
        {
            var facilityRigPresets = SdeFacilityRigCatalog.Load(sdeDirectory);
            FacilityFittingCalculator.UseRigPresets(facilityRigPresets);
            var sdeProvider = new SdeIndustryDataProvider(sdeDirectory);
            var marketPriceProvider = new CompositeMarketPriceProvider(
                new FuzzworksMarketPriceProvider(GetMarketCacheFilePath()),
                sdeProvider);

            return new DefaultServices(
                sdeProvider,
                sdeProvider,
                new EsiIndustryCostIndexProvider(GetIndustryCostIndexCacheFilePath()),
                new ManufacturingCalculator(sdeProvider, marketPriceProvider),
                new ShoppingListService(),
                userSettingsService,
                facilityRigPresets,
                $"SDE loaded from {sdeDirectory}; {facilityRigPresets.Count - 1} facility rigs loaded; prices use Fuzzworks with SDE fallback");
        }

        var sampleBlueprintRepository = new SampleBlueprintRepository();
        return new DefaultServices(
            sampleBlueprintRepository,
            new SampleSolarSystemRepository(),
            new SampleIndustryCostIndexProvider(),
            new ManufacturingCalculator(sampleBlueprintRepository, new SampleMarketPriceProvider()),
            new ShoppingListService(),
            userSettingsService,
            FacilityFittingCalculator.RigPresets,
            "Sample data loaded");
    }

    private static string? FindSdeDirectory()
    {
        var candidates = new[]
        {
            SdeDownloadService.DefaultSdeDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "sde")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "sde")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "src", "sde")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "sde"))
        };

        return candidates.FirstOrDefault(candidate => SdeIndustryDataProvider.IsAvailable(candidate));
    }

    private static string GetMarketCacheFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "EveIndustryPlanner", "fuzzworks-prices.json");
    }

    private static string GetIndustryCostIndexCacheFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "EveIndustryPlanner", "industry-cost-indexes.json");
    }

    private static string GetSettingsFilePath()
    {
        return UserSettingsService.GetDefaultSettingsFilePath();
    }

    private MarketLocationOption FindMarketLocation(long locationId)
    {
        return MarketLocations.FirstOrDefault(location => location.LocationId == locationId)
            ?? MarketLocations.First(location => location.LocationId == PriceProfile.DefaultMarketLocationId);
    }

    private MarketPriceStrategyOption FindMaterialPriceStrategy(MarketPriceSelection selection)
    {
        return MaterialPriceStrategies.FirstOrDefault(strategy => strategy.Selection == selection)
            ?? MaterialPriceStrategies.First(strategy => strategy.Selection == MarketPriceSelection.InstantBuy);
    }

    private MarketPriceStrategyOption FindProductPriceStrategy(MarketPriceSelection selection)
    {
        return ProductPriceStrategies.FirstOrDefault(strategy => strategy.Selection == selection)
            ?? ProductPriceStrategies.First(strategy => strategy.Selection == MarketPriceSelection.InstantSell);
    }

    private BuildBuyDepthOption FindBuildBuyDepth(BuildBuyDepth depth)
    {
        return BuildBuyDepthOptions.FirstOrDefault(option => option.Depth == depth)
            ?? BuildBuyDepthOptions.First(option => option.Depth == BuildBuyDepth.DirectMaterialsOnly);
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "<empty>";
        }

        return token.Length <= 12
            ? $"{token[..Math.Min(4, token.Length)]}..."
            : $"{token[..6]}...{token[^4..]}";
    }

    private void LoadFacilityProfiles(UserSettings settings)
    {
        FacilityProfiles.Clear();
        var profiles = settings.FacilityProfiles.Count == 0
            ? [SavedFacilityProfile.None]
            : settings.FacilityProfiles;

        foreach (var profile in profiles)
        {
            FacilityProfiles.Add(FacilityOption.FromSaved(profile));
        }

        if (!FacilityProfiles.Any(profile => profile.Id == SavedFacilityProfile.NoneId))
        {
            FacilityProfiles.Insert(0, FacilityOption.FromSaved(SavedFacilityProfile.None));
        }
    }

    private void LoadCharacterAccounts(UserSettings settings)
    {
        CharacterAccounts.Clear();
        foreach (var account in settings.CharacterAccounts.OrderBy(account => account.CharacterName, StringComparer.OrdinalIgnoreCase))
        {
            CharacterAccounts.Add(CharacterAccountOption.FromSaved(account));
        }

        SelectedCharacterAccount = CharacterAccounts.FirstOrDefault();
    }

    private async Task AddCharacterAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "Waiting for EVE SSO login";
            var token = await characterAuthService.AddCharacterViaPocketBaseAsync(
                PocketBaseUrl,
                DefaultCharacterScopes,
                SelectedCharacterAccount?.PocketBaseAuthToken);
            UpsertCharacterAccount(token);
            SaveUserSettings();
            StatusText = $"Character added: {token.CharacterName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not add character: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DeleteSelectedCharacter()
    {
        if (SelectedCharacterAccount is null)
        {
            return;
        }

        var deletedName = SelectedCharacterAccount.CharacterName;
        CharacterAccounts.Remove(SelectedCharacterAccount);
        SelectedCharacterAccount = CharacterAccounts.FirstOrDefault();
        SaveUserSettings();
        StatusText = $"Character removed: {deletedName}";
    }

    private void UpsertCharacterAccount(EveSsoCharacterToken token, DateTimeOffset? existingAddedAt = null)
    {
        var account = new CharacterAccountOption(
            token.CharacterId,
            token.CharacterName,
            token.PocketBaseAuthToken,
            token.Scopes.OrderBy(scope => scope, StringComparer.Ordinal).ToList(),
            existingAddedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var existing = CharacterAccounts.FirstOrDefault(character => character.CharacterId == account.CharacterId);
        if (existing is not null)
        {
            var index = CharacterAccounts.IndexOf(existing);
            CharacterAccounts[index] = account;
        }
        else
        {
            CharacterAccounts.Add(account);
        }

        SelectedCharacterAccount = account;
        OnPropertyChanged(nameof(SelectedCharacterScopesText));
        OnPropertyChanged(nameof(SelectedCharacterTokenText));
    }

    private FacilityOption FindFacility(Guid facilityId)
    {
        return FacilityProfiles.FirstOrDefault(profile => profile.Id == facilityId)
            ?? FacilityProfiles.First(profile => profile.Id == SavedFacilityProfile.NoneId);
    }

    private FacilitySecurityOption FindSecurityBand(double securityStatus)
    {
        var securityBand = securityStatus <= 0.0
            ? FacilitySecurityBand.NullSec
            : securityStatus < 0.45
                ? FacilitySecurityBand.LowSec
                : FacilitySecurityBand.HighSec;

        return FacilitySecurityBands.First(option => option.SecurityBand == securityBand);
    }

    private async Task LoadServerDataAsync()
    {
        if (SelectedCharacterAccount is null && CharacterAccounts.Count == 0)
        {
            return;
        }

        try
        {
            var snapshot = await dataService.LoadSnapshotAsync();
            if (snapshot is null)
            {
                return;
            }

            isApplyingServerData = true;
            if (snapshot.Facilities.Count > 0)
            {
                LoadFacilityProfiles(new UserSettings { FacilityProfiles = snapshot.Facilities });
            }

            if (snapshot.Settings is not null)
            {
                ApplyServerUserSettings(snapshot.Settings);
            }

            if (snapshot.ProductionLedgers.Count > 0)
            {
                LoadProductionLedgerOptions(snapshot.ProductionLedgers);
                if (ProductionLedgerEntries.Count == 0)
                {
                    SelectedProductionLedger = ProductionLedgers.FirstOrDefault();
                }
            }

            foreach (var cacheEntry in snapshot.MarketScanCache.Where(entry => entry.ExpiresAt > DateTimeOffset.UtcNow))
            {
                marketScannerCache[cacheEntry.CacheKey] = new MarketScannerCacheEntry(DateTimeOffset.UtcNow, cacheEntry.Rows);
            }

            StatusText = "Server data loaded";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            StatusText = $"Could not load server data: {ex.Message}";
        }
        finally
        {
            isApplyingServerData = false;
        }
    }

    private void ApplyServerUserSettings(ServerUserSettings settings)
    {
        selectedMaterialMarket = FindMarketLocation(settings.MaterialMarketLocationId);
        selectedProductMarket = FindMarketLocation(settings.ProductMarketLocationId);
        selectedMaterialPriceStrategy = FindMaterialPriceStrategy(settings.MaterialPriceSelection);
        selectedProductPriceStrategy = FindProductPriceStrategy(settings.ProductPriceSelection);
        selectedBuildBuyDepth = FindBuildBuyDepth(settings.BuildBuyDepth);
        EnableBuildBuy = settings.EnableBuildBuy;
        MaxBuildBuyDepth = settings.MaxBuildBuyDepth;
        SelectedFinalProductFacility = FindFacility(settings.FinalProductFacilityId);
        SelectedComponentFacility = FindFacility(settings.ComponentFacilityId);
        SelectedReactionFacility = FindFacility(settings.ReactionFacilityId);
        OnPropertyChanged(nameof(SelectedMaterialMarket));
        OnPropertyChanged(nameof(SelectedProductMarket));
        OnPropertyChanged(nameof(SelectedMaterialPriceStrategy));
        OnPropertyChanged(nameof(SelectedProductPriceStrategy));
        OnPropertyChanged(nameof(SelectedBuildBuyDepth));
    }

    private ServerUserSettings CreateServerUserSettings()
    {
        return new ServerUserSettings
        {
            FinalProductFacilityId = SelectedFinalProductFacility.Id,
            ComponentFacilityId = SelectedComponentFacility.Id,
            ReactionFacilityId = SelectedReactionFacility.Id,
            MaterialMarketLocationId = SelectedMaterialMarket.LocationId,
            ProductMarketLocationId = SelectedProductMarket.LocationId,
            MaterialPriceSelection = SelectedMaterialPriceStrategy.Selection,
            ProductPriceSelection = SelectedProductPriceStrategy.Selection,
            EnableBuildBuy = EnableBuildBuy,
            BuildBuyDepth = SelectedBuildBuyDepth.Depth,
            MaxBuildBuyDepth = MaxBuildBuyDepth
        };
    }

    private void SaveUserSettings()
    {
        userSettingsService.Save(new UserSettings
        {
            FacilityProfiles = FacilityProfiles.Select(profile => profile.ToSaved()).ToList(),
            CharacterAccounts = CharacterAccounts.Select(account => account.ToSaved()).ToList(),
            FinalProductFacilityId = SelectedFinalProductFacility.Id,
            ComponentFacilityId = SelectedComponentFacility.Id,
            ReactionFacilityId = SelectedReactionFacility.Id,
            MaterialMarketLocationId = SelectedMaterialMarket.LocationId,
            ProductMarketLocationId = SelectedProductMarket.LocationId,
            MaterialPriceSelection = SelectedMaterialPriceStrategy.Selection,
            ProductPriceSelection = SelectedProductPriceStrategy.Selection,
            EnableBuildBuy = EnableBuildBuy,
            BuildBuyDepth = SelectedBuildBuyDepth.Depth,
            MaxBuildBuyDepth = MaxBuildBuyDepth,
            PocketBaseUrl = PocketBaseUrl
        });

        if (!isApplyingServerData)
        {
            _ = SaveServerUserDataAsync();
        }
    }

    private async Task SaveServerUserDataAsync()
    {
        if (SelectedCharacterAccount is null && CharacterAccounts.Count == 0)
        {
            return;
        }

        try
        {
            await dataService.SaveSettingsAsync(CreateServerUserSettings());
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            StatusText = $"Could not save server settings: {ex.Message}";
            return;
        }

        try
        {
            await dataService.SaveFacilitiesAsync(
                FacilityProfiles
                    .Where(profile => profile.Id != SavedFacilityProfile.NoneId)
                    .Select(profile => profile.ToSaved())
                    .ToList());
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException)
        {
            StatusText = $"Could not save server facilities: {ex.Message}";
        }
    }

    private sealed record DefaultServices(
        IBlueprintRepository BlueprintRepository,
        ISolarSystemRepository SolarSystemRepository,
        IIndustryCostIndexProvider IndustryCostIndexProvider,
        IManufacturingCalculator Calculator,
        IShoppingListService ShoppingListService,
        UserSettingsService UserSettingsService,
        IReadOnlyList<FacilityRigPreset> FacilityRigPresets,
        string StatusText);

    public sealed record MarketLocationOption(string Name, long LocationId);

    public sealed record MarketPriceStrategyOption(string Name, MarketPriceSelection Selection);

    public sealed record BuildBuyDepthOption(string Name, BuildBuyDepth Depth);

    public sealed record ProductionLedgerOption(string Id, string Name, ServerProductionLedgerDocument Document);

    public enum ProductionPlannerScope
    {
        AllLedgerItems,
        SelectedLedgerItem
    }

    public enum ProductionJobFilter
    {
        Open,
        All,
        Completed
    }

    public sealed record ProductionPlannerScopeOption(string Name, ProductionPlannerScope Scope);

    public sealed record ProductionJobFilterOption(string Name, ProductionJobFilter Filter);

    public enum MarketScannerFilter
    {
        All,
        Reactions,
        Components,
        Ships,
        Modules,
        DronesAndFighters,
        Charges,
        Structures,
        Other
    }

    public sealed record MarketScannerFilterOption(string Name, MarketScannerFilter Filter);

    public enum MarketScannerTechFilter
    {
        All,
        Tech1,
        Tech2,
        Tech3,
        Faction,
        Storyline,
        Officer,
        Deadspace,
        OtherMeta
    }

    public sealed record MarketScannerTechFilterOption(string Name, MarketScannerTechFilter Filter);

    public sealed record FacilityStructureOption(string Id, string Name, int RigSlots, FacilityRigSize RigSize);

    public sealed record FacilityServiceOption(string Id, string Name, FacilityServiceRole Role);

    public sealed record FacilitySecurityOption(string Name, FacilitySecurityBand SecurityBand);

    public sealed record FacilityRigOption(string Id, string Name, FacilityRigSize RigSize, FacilityServiceRole Role, string ScopeName)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(ScopeName) ? Name : $"{Name} [{ScopeName}]";
    }

    public sealed record SolarSystemOption(long SolarSystemId, string Name, double SecurityStatus, long RegionId)
    {
        public string DisplayName => $"{Name} ({SolarSystemId}, {SecurityStatus:0.0})";
    }

    public sealed record CharacterAccountOption(
        long CharacterId,
        string CharacterName,
        string PocketBaseAuthToken,
        IReadOnlyList<string> Scopes,
        DateTimeOffset AddedAt,
        DateTimeOffset UpdatedAt)
    {
        public string TokenExpiresText => "Server managed";
        public string AddedAtText => AddedAt.ToLocalTime().ToString("g");
        public string ScopesPreview => Scopes.Count == 0 ? "No scopes" : string.Join(", ", Scopes.Take(3)) + (Scopes.Count > 3 ? $" +{Scopes.Count - 3}" : string.Empty);

        public SavedCharacterAccount ToSaved()
        {
            return new SavedCharacterAccount
            {
                CharacterId = CharacterId,
                CharacterName = CharacterName,
                PocketBaseAuthToken = PocketBaseAuthToken,
                Scopes = Scopes.ToList(),
                AddedAt = AddedAt,
                UpdatedAt = UpdatedAt
            };
        }

        public static CharacterAccountOption FromSaved(SavedCharacterAccount account)
        {
            return new CharacterAccountOption(
                account.CharacterId,
                account.CharacterName,
                account.PocketBaseAuthToken,
                account.Scopes,
                account.AddedAt,
                account.UpdatedAt);
        }
    }

    public sealed class ProductionLedgerEntry
    {
        public ProductionLedgerEntry()
        {
        }

        public ProductionLedgerEntry(
            Guid id,
            DateTimeOffset addedAt,
            long blueprintId,
            string blueprintName,
            string productName,
            int runs,
            int lines,
            int materialEfficiency,
            int timeEfficiency,
            string materialMarketName,
            string productMarketName,
            string finalProductFacilityName,
            string componentFacilityName,
            string reactionFacilityName,
            ManufacturingResult result)
        {
            Id = id;
            AddedAt = addedAt;
            BlueprintId = blueprintId;
            BlueprintName = blueprintName;
            ProductName = productName;
            Runs = runs;
            Lines = lines;
            MaterialEfficiency = materialEfficiency;
            TimeEfficiency = timeEfficiency;
            MaterialMarketName = materialMarketName;
            ProductMarketName = productMarketName;
            FinalProductFacilityName = finalProductFacilityName;
            ComponentFacilityName = componentFacilityName;
            ReactionFacilityName = reactionFacilityName;
            Result = result;
        }

        public Guid Id { get; set; }
        public DateTimeOffset AddedAt { get; set; }
        public long BlueprintId { get; set; }
        public string BlueprintName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Runs { get; set; }
        public int Lines { get; set; } = 1;
        public int MaterialEfficiency { get; set; }
        public int TimeEfficiency { get; set; }
        public string MaterialMarketName { get; set; } = string.Empty;
        public string ProductMarketName { get; set; } = string.Empty;
        public string FinalProductFacilityName { get; set; } = string.Empty;
        public string ComponentFacilityName { get; set; } = string.Empty;
        public string ReactionFacilityName { get; set; } = string.Empty;
        public ManufacturingResult Result { get; set; } = new();
        public string OutputText => $"{Result.OutputQuantity:N0} x {ProductName}";
        public string SummaryText => $"{Runs:N0} runs/job x {Lines:N0} lines / cost {Result.TotalCost:N2} ISK / profit {Result.Profit:N2} ISK";
    }

    public sealed record MarketScannerResultRow(
        long BlueprintId,
        long ProductTypeId,
        string ProductName,
        string BlueprintName,
        string ItemType,
        string TechMeta,
        string Activity,
        long OutputQuantity,
        decimal MaterialCost,
        decimal JobCost,
        decimal TotalCost,
        decimal Revenue,
        decimal Profit,
        decimal Margin,
        decimal IskPerHour,
        TimeSpan ProductionTime,
        int WarningCount,
        int MissingPriceCount,
        int LowVolumeCount,
        string Error = "")
    {
        public string ProductionTimeText => FormatDuration(ProductionTime);
        public string Status
        {
            get
            {
                if (Error.Length > 0)
                {
                    return Error;
                }

                var issues = new List<string>();
                if (MissingPriceCount > 0)
                {
                    issues.Add($"{MissingPriceCount:N0} missing price");
                }

                if (LowVolumeCount > 0)
                {
                    issues.Add($"{LowVolumeCount:N0} low volume");
                }

                if (WarningCount > MissingPriceCount + LowVolumeCount)
                {
                    issues.Add($"{WarningCount - MissingPriceCount - LowVolumeCount:N0} warnings");
                }

                return issues.Count == 0 ? "OK" : string.Join(", ", issues);
            }
        }
    }

    private sealed record MarketScannerCacheEntry(DateTimeOffset CreatedAt, IReadOnlyList<MarketScannerResultRow> Rows);

    private sealed record ProductionJobRequirementSource(
        Guid SourceEntryId,
        string SourceProductName,
        ProductionJobRequirement Requirement,
        int DependencyStage,
        int SplitGroupIndex,
        int JobIndex);

    private sealed record AggregatedProductionJobRequirement(
        string SourceProductName,
        string ParentProductName,
        BlueprintId BlueprintId,
        string BlueprintName,
        TypeId ProductTypeId,
        string ProductName,
        BlueprintActivityType ActivityType,
        string FacilityName,
        long RequiredQuantity,
        int OutputQuantityPerRun,
        int TotalRuns,
        TimeSpan TimePerRun,
        int DependencyStage,
        int Depth,
        int SplitGroupIndex,
        int JobIndex);

    private sealed record ExpandedProductionJobRequirement(
        AggregatedProductionJobRequirement Requirement,
        int Runs,
        TimeSpan Duration,
        int SplitIndex);

    public sealed class PlannedProductionJobRow
    {
        private readonly Action<PlannedProductionJobRow> stateChanged;
        private bool isCompleted;
        private string notes = string.Empty;

        public PlannedProductionJobRow(
            string stableKey,
            string sourceProductName,
            string productName,
            string blueprintName,
            BlueprintActivityType activityType,
            string facilityName,
            string parentProductName,
            long requiredQuantity,
            int outputQuantityPerRun,
            int totalRuns,
            int dependencyStage,
            int jobIndex,
            int jobCount,
            int runs,
            TimeSpan timePerRun,
            TimeSpan duration,
            int depth,
            Action<PlannedProductionJobRow> stateChanged)
        {
            StableKey = stableKey;
            SourceProductName = sourceProductName;
            ProductName = productName;
            BlueprintName = blueprintName;
            ActivityType = activityType;
            FacilityName = facilityName;
            ParentProductName = parentProductName;
            RequiredQuantity = requiredQuantity;
            OutputQuantityPerRun = outputQuantityPerRun;
            TotalRuns = totalRuns;
            DependencyStage = dependencyStage;
            Wave = dependencyStage;
            JobIndex = jobIndex;
            JobCount = jobCount;
            Runs = runs;
            TimePerRun = timePerRun;
            Duration = duration;
            Depth = depth;
            this.stateChanged = stateChanged;
        }

        public string StableKey { get; }
        public string SourceProductName { get; }
        public string ProductName { get; }
        public string BlueprintName { get; }
        public BlueprintActivityType ActivityType { get; }
        public string FacilityName { get; }
        public string ParentProductName { get; }
        public long RequiredQuantity { get; }
        public int OutputQuantityPerRun { get; }
        public int TotalRuns { get; }
        public int DependencyStage { get; }
        public int Wave { get; private set; }
        public int JobIndex { get; }
        public int JobCount { get; }
        public int Runs { get; }
        public TimeSpan TimePerRun { get; }
        public TimeSpan Duration { get; }
        public int Depth { get; }
        public string ActivityName => ActivityType == BlueprintActivityType.Reaction ? "Reaction" : "Manufacturing";
        public string DurationText => FormatDuration(Duration);
        public string TimePerRunText => FormatDuration(TimePerRun);
        public string JobLabel => $"{JobIndex:N0}/{JobCount:N0}";
        public string WaveLabel => $"Wave {Wave:N0}";

        public void SetWave(int wave)
        {
            Wave = wave;
        }

        public void ApplyState(bool completed, DateTimeOffset? completedAt, string noteText)
        {
            isCompleted = completed;
            CompletedAt = completedAt;
            notes = noteText;
        }

        public bool IsCompleted
        {
            get => isCompleted;
            set
            {
                if (isCompleted == value)
                {
                    return;
                }

                isCompleted = value;
                CompletedAt = value ? DateTimeOffset.Now : null;
                stateChanged(this);
            }
        }

        public DateTimeOffset? CompletedAt { get; set; }

        public string Notes
        {
            get => notes;
            set
            {
                notes = value ?? string.Empty;
                stateChanged(this);
            }
        }
    }

    public sealed record ProductionJobCompletionState(
        string StableKey,
        bool IsCompleted,
        DateTimeOffset? CompletedAt,
        string Notes);

    public sealed class ProductionPlannerSettings
    {
        public ProductionPlannerScope Scope { get; init; } = ProductionPlannerScope.AllLedgerItems;
        public ProductionJobFilter Filter { get; init; } = ProductionJobFilter.Open;
        public decimal MaxManufacturingJobHours { get; init; } = 48m;
        public decimal MaxReactionJobHours { get; init; } = 24m;
        public int MaxParallelManufacturingJobs { get; init; } = 10;
        public int MaxParallelReactionJobs { get; init; } = 5;
    }

    public sealed record FacilityOption(
        Guid Id,
        string Name,
        long SolarSystemId,
        string SolarSystemName,
        string StructureId,
        string StructureName,
        FacilityServiceRole ServiceRole,
        IReadOnlyList<string> ServiceModuleIds,
        FacilitySecurityBand SecurityBand,
        IReadOnlyList<string> RigIds,
        decimal MaterialMultiplier,
        decimal TimeMultiplier,
        decimal JobCostMultiplier,
        decimal SystemCostIndex,
        decimal FacilityTaxRate)
    {
        public string DisplayName => $"{Name} ({SolarSystemName}, {StructureName})";

        public FacilityProfile ToFacilityProfile()
        {
            return new FacilityProfile
            {
                Name = Name,
                SolarSystemId = SolarSystemId,
                SolarSystemName = SolarSystemName,
                StructureId = StructureId,
                StructureName = StructureName,
                ServiceRole = ServiceRole,
                ServiceModuleIds = ServiceModuleIds,
                SecurityBand = SecurityBand,
                RigIds = RigIds,
                MaterialMultiplier = MaterialMultiplier,
                TimeMultiplier = TimeMultiplier,
                JobCostMultiplier = JobCostMultiplier,
                SystemCostIndex = SystemCostIndex,
                FacilityTaxRate = FacilityTaxRate
            };
        }

        public SavedFacilityProfile ToSaved()
        {
            return new SavedFacilityProfile
            {
                Id = Id,
                Name = Name,
                SolarSystemId = SolarSystemId,
                SolarSystemName = SolarSystemName,
                StructureId = StructureId,
                StructureName = StructureName,
                ServiceRole = ServiceRole,
                ServiceModuleIds = ServiceModuleIds.ToList(),
                SecurityBand = SecurityBand,
                RigIds = RigIds.ToList(),
                MaterialMultiplier = MaterialMultiplier,
                TimeMultiplier = TimeMultiplier,
                JobCostMultiplier = JobCostMultiplier,
                SystemCostIndex = SystemCostIndex,
                FacilityTaxRate = FacilityTaxRate
            };
        }

        public static FacilityOption FromSaved(SavedFacilityProfile profile)
        {
            return new FacilityOption(
                profile.Id,
                profile.Name,
                profile.SolarSystemId,
                profile.SolarSystemName,
                profile.StructureId,
                profile.StructureName,
                profile.ServiceRole,
                profile.ServiceModuleIds,
                profile.SecurityBand,
                profile.RigIds,
                profile.MaterialMultiplier,
                profile.TimeMultiplier,
                profile.JobCostMultiplier,
                profile.SystemCostIndex,
                profile.FacilityTaxRate);
        }
    }
}
