using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using EveIndustryPlanner.App.ViewModels;
using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.App;

public partial class ProductionAssetsDialog : Window
{
    private readonly ProductionAssetsDialogViewModel viewModel;

    public ProductionAssetsDialog(
        EveAssetService assetService,
        IReadOnlyList<SavedCharacterAccount> accounts,
        IReadOnlyList<ShoppingListLine> neededLines)
    {
        InitializeComponent();
        viewModel = new ProductionAssetsDialogViewModel(assetService, accounts, neededLines, this);
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync(refresh: false);
    }

    public IReadOnlyDictionary<TypeId, long> SelectedDeductions => viewModel.SelectedDeductions;
}

public sealed class ProductionAssetsDialogViewModel : ObservableObject
{
    private readonly EveAssetService assetService;
    private readonly IReadOnlyList<SavedCharacterAccount> accounts;
    private readonly Dictionary<long, ShoppingListLine> neededByTypeId;
    private readonly Window owner;
    private List<CachedEveAsset> allAssets = [];
    private string searchText = string.Empty;
    private string locationFilterText = string.Empty;
    private bool neededOnly = true;
    private string summaryText = "Loading assets";
    private string appliedSummaryText = string.Empty;
    private bool isBusy;

    public ProductionAssetsDialogViewModel(
        EveAssetService assetService,
        IReadOnlyList<SavedCharacterAccount> accounts,
        IReadOnlyList<ShoppingListLine> neededLines,
        Window owner)
    {
        this.assetService = assetService;
        this.accounts = accounts;
        this.owner = owner;
        neededByTypeId = neededLines.ToDictionary(line => line.TypeId.Value, line => line);

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(refresh: true), () => !IsBusy);
        SelectNeededCommand = new RelayCommand(SelectNeededLocations, () => Systems.Count > 0);
        ApplyCommand = new RelayCommand(ApplySelected);
    }

    public ObservableCollection<ProductionAssetTreeNode> Systems { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand SelectNeededCommand { get; }

    public ICommand ApplyCommand { get; }

    public IReadOnlyDictionary<TypeId, long> SelectedDeductions { get; private set; } = new Dictionary<TypeId, long>();

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                RebuildTree();
            }
        }
    }

    public string LocationFilterText
    {
        get => locationFilterText;
        set
        {
            if (SetProperty(ref locationFilterText, value))
            {
                RebuildTree();
            }
        }
    }

    public bool NeededOnly
    {
        get => neededOnly;
        set
        {
            if (SetProperty(ref neededOnly, value))
            {
                RebuildTree();
            }
        }
    }

    public string SummaryText
    {
        get => summaryText;
        private set => SetProperty(ref summaryText, value);
    }

    public string AppliedSummaryText
    {
        get => appliedSummaryText;
        private set => SetProperty(ref appliedSummaryText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value) && RefreshCommand is AsyncRelayCommand refreshCommand)
            {
                refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task LoadAsync(bool refresh)
    {
        try
        {
            IsBusy = true;
            SummaryText = refresh ? "Refreshing assets from ESI" : "Loading cached assets";

            allAssets = (await assetService.LoadAllCharacterAssetsAsync(accounts, refresh)).ToList();
            RebuildTree();
            SelectNeededLocations();
            SummaryText = $"{Systems.Sum(system => system.CountDescendants("Structure")):N0} structures / {Systems.Sum(system => system.CountMatchingLeaves()):N0} match ledger";
        }
        catch (Exception ex)
        {
            SummaryText = $"Could not load assets: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildTree()
    {
        Systems.Clear();
        var assetsByItemId = allAssets.ToDictionary(asset => asset.ItemId);
        var rootNodes = new Dictionary<string, ProductionAssetTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in allAssets)
        {
            var item = ToLocationItem(asset);
            if (NeededOnly && item.NeededQuantity <= 0)
            {
                continue;
            }

            if (!MatchesSearch(asset, item) || !MatchesLocation(asset))
            {
                continue;
            }

            var systemName = string.IsNullOrWhiteSpace(asset.SolarSystemName) ? "Unknown System" : asset.SolarSystemName;
            var systemNode = GetOrAddNode(rootNodes, Systems, $"system:{systemName}", systemName, "System", UpdateAppliedSummary);

            var structureName = string.IsNullOrWhiteSpace(asset.RootLocationName) ? $"Location {asset.RootLocationId}" : asset.RootLocationName;
            var structureNode = GetOrAddNode(systemNode.ChildrenByKey, systemNode.Children, $"structure:{asset.RootLocationId}", structureName, "Structure", UpdateAppliedSummary);

            var currentNode = structureNode;
            foreach (var containerName in BuildContainerPath(asset, assetsByItemId))
            {
                currentNode = GetOrAddNode(currentNode.ChildrenByKey, currentNode.Children, $"container:{containerName}", containerName, "Container", UpdateAppliedSummary);
            }

            AddItemToPath(currentNode, item);
            AddItemToPath(structureNode, item);
            AddItemToPath(systemNode, item);
            AddLeafItemNode(currentNode, item);
        }

        foreach (var system in Systems)
        {
            system.SortChildren();
        }
    }

    private ProductionAssetLocationItem ToLocationItem(CachedEveAsset asset)
    {
        neededByTypeId.TryGetValue(asset.TypeId, out var neededLine);
        return new ProductionAssetLocationItem(
            asset.TypeId,
            string.IsNullOrWhiteSpace(asset.TypeName) ? neededLine?.Name ?? $"Type {asset.TypeId}" : asset.TypeName,
            asset.Quantity,
            neededLine?.Quantity ?? 0);
    }

    private bool MatchesSearch(CachedEveAsset asset, ProductionAssetLocationItem item)
    {
        return string.IsNullOrWhiteSpace(SearchText)
            || item.TypeName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || asset.TypeId.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesLocation(CachedEveAsset asset)
    {
        return string.IsNullOrWhiteSpace(LocationFilterText)
            || asset.SolarSystemName.Contains(LocationFilterText, StringComparison.OrdinalIgnoreCase)
            || asset.RootLocationName.Contains(LocationFilterText, StringComparison.OrdinalIgnoreCase)
            || asset.LocationName.Contains(LocationFilterText, StringComparison.OrdinalIgnoreCase)
            || asset.RootLocationId.ToString().Contains(LocationFilterText, StringComparison.OrdinalIgnoreCase)
            || asset.LocationId.ToString().Contains(LocationFilterText, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> BuildContainerPath(CachedEveAsset asset, IReadOnlyDictionary<long, CachedEveAsset> assetsByItemId)
    {
        var names = new List<string>();
        var locationId = asset.LocationId;
        var guard = 0;
        while (assetsByItemId.TryGetValue(locationId, out var parentAsset) && guard < 64)
        {
            names.Add(string.IsNullOrWhiteSpace(parentAsset.ItemName) ? parentAsset.TypeName : parentAsset.ItemName);
            locationId = parentAsset.LocationId;
            guard++;
        }

        names.Reverse();
        return names;
    }

    private static ProductionAssetTreeNode GetOrAddNode(
        IDictionary<string, ProductionAssetTreeNode> lookup,
        ObservableCollection<ProductionAssetTreeNode> collection,
        string key,
        string name,
        string nodeType,
        Action changed)
    {
        if (lookup.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var node = new ProductionAssetTreeNode(name, nodeType, changed);
        lookup[key] = node;
        collection.Add(node);
        return node;
    }

    private static void AddItemToPath(ProductionAssetTreeNode node, ProductionAssetLocationItem item)
    {
        node.AddItem(item);
    }

    private static void AddLeafItemNode(ProductionAssetTreeNode parentNode, ProductionAssetLocationItem item)
    {
        var key = $"item:{item.TypeId}";
        if (!parentNode.ChildrenByKey.TryGetValue(key, out var itemNode))
        {
            itemNode = new ProductionAssetTreeNode(item.TypeName, "Item", static () => { });
            parentNode.ChildrenByKey[key] = itemNode;
            parentNode.Children.Add(itemNode);
        }

        itemNode.AddItem(item);
    }

    private void SelectNeededLocations()
    {
        foreach (var system in Systems)
        {
            system.IsSelected = system.NeededTypeCount > 0;
        }

        UpdateAppliedSummary();
    }

    private void ApplySelected()
    {
        SelectedDeductions = BuildCappedDeductions();

        owner.DialogResult = true;
        owner.Close();
    }

    private void UpdateAppliedSummary()
    {
        var deductions = BuildCappedDeductions().Values.Sum();

        AppliedSummaryText = $"{deductions:N0} units selected for deduction";
    }

    private Dictionary<TypeId, long> BuildCappedDeductions()
    {
        var selectedByTypeId = GetSelectedTopNodes(Systems, ancestorSelected: false)
            .SelectMany(node => node.Items)
            .GroupBy(item => item.TypeId);

        var deductions = new Dictionary<TypeId, long>();
        foreach (var group in selectedByTypeId)
        {
            if (!neededByTypeId.TryGetValue(group.Key, out var neededLine))
            {
                continue;
            }

            var selectedQuantity = group.Sum(item => item.Quantity);
            var deduction = Math.Min(neededLine.Quantity, selectedQuantity);
            if (deduction > 0)
            {
                deductions[new TypeId(group.Key)] = deduction;
            }
        }

        return deductions;
    }

    private static IEnumerable<ProductionAssetTreeNode> GetSelectedTopNodes(
        IEnumerable<ProductionAssetTreeNode> nodes,
        bool ancestorSelected)
    {
        foreach (var node in nodes)
        {
            if (node.IsSelected && !ancestorSelected)
            {
                yield return node;
            }

            foreach (var child in GetSelectedTopNodes(node.Children, ancestorSelected || node.IsSelected))
            {
                yield return child;
            }
        }
    }
}

public sealed class ProductionAssetTreeNode : ObservableObject
{
    private bool isSelected;
    private readonly Action changed;

    public ProductionAssetTreeNode(string name, string nodeType, Action changed)
    {
        Name = name;
        NodeType = nodeType;
        this.changed = changed;
    }

    public string Name { get; }
    public string NodeType { get; }
    public ObservableCollection<ProductionAssetTreeNode> Children { get; } = [];
    public Dictionary<string, ProductionAssetTreeNode> ChildrenByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ProductionAssetLocationItem> Items { get; } = [];
    public FontWeight HeaderWeight => NodeType == "System" ? FontWeights.SemiBold : FontWeights.Normal;
    public Visibility SelectionVisibility => NodeType == "Item" ? Visibility.Hidden : Visibility.Visible;
    public long TotalQuantity => Items.Sum(item => item.Quantity);
    public int NeededTypeCount => Items.Where(item => item.NeededQuantity > 0).Select(item => item.TypeId).Distinct().Count();
    public long AppliedQuantity => IsSelected ? Items.Sum(item => Math.Min(item.Quantity, item.NeededQuantity)) : 0;
    public string MatchingItemsPreview
    {
        get
        {
            var matchingItems = Items
                .Where(item => item.NeededQuantity > 0)
                .Select(item => $"{item.TypeName} x{Math.Min(item.Quantity, item.NeededQuantity):N0}")
                .Take(4)
                .ToList();

            if (matchingItems.Count == 0)
            {
                return Items.Count == 0 ? string.Empty : $"{Items.Count:N0} asset types";
            }

            var suffix = NeededTypeCount > matchingItems.Count ? $" +{NeededTypeCount - matchingItems.Count:N0}" : string.Empty;
            return string.Join(", ", matchingItems) + suffix;
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                OnPropertyChanged(nameof(AppliedQuantity));
                foreach (var child in Children)
                {
                    child.IsSelected = value;
                }
                changed();
            }
        }
    }

    public void AddItem(ProductionAssetLocationItem item)
    {
        Items.Add(item);
        OnPropertyChanged(nameof(TotalQuantity));
        OnPropertyChanged(nameof(NeededTypeCount));
        OnPropertyChanged(nameof(AppliedQuantity));
        OnPropertyChanged(nameof(MatchingItemsPreview));
    }

    public void SortChildren()
    {
        var sorted = Children.OrderBy(child => child.Name).ToList();
        Children.Clear();
        foreach (var child in sorted)
        {
            child.SortChildren();
            Children.Add(child);
        }
    }

    public int CountDescendants(string nodeType)
    {
        return Children.Count(child => child.NodeType == nodeType)
            + Children.Sum(child => child.CountDescendants(nodeType));
    }

    public int CountMatchingLeaves()
    {
        if (Children.Count == 0)
        {
            return NeededTypeCount > 0 ? 1 : 0;
        }

        return Children.Sum(child => child.CountMatchingLeaves());
    }
}

public sealed record ProductionAssetLocationItem(
    long TypeId,
    string TypeName,
    long Quantity,
    long NeededQuantity);
