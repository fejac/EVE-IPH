using System.Collections.ObjectModel;
using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.App.ViewModels;

public sealed class ProductionTreeNodeViewModel : ObservableObject
{
    private readonly Action<BlueprintId, int, int> efficiencyChanged;
    private int materialEfficiency;
    private int timeEfficiency;

    public ProductionTreeNodeViewModel(
        BlueprintId blueprintId,
        BlueprintId? parentBlueprintId,
        string productName,
        BlueprintActivityType activityType,
        string facilityName,
        long requiredQuantity,
        int totalRuns,
        TimeSpan totalTime,
        int depth,
        int materialEfficiency,
        int timeEfficiency,
        Action<BlueprintId, int, int> efficiencyChanged)
    {
        BlueprintId = blueprintId;
        ParentBlueprintId = parentBlueprintId;
        ProductName = productName;
        ActivityType = activityType;
        FacilityName = facilityName;
        RequiredQuantity = requiredQuantity;
        TotalRuns = totalRuns;
        TotalTime = totalTime;
        Depth = depth;
        this.materialEfficiency = materialEfficiency;
        this.timeEfficiency = timeEfficiency;
        this.efficiencyChanged = efficiencyChanged;
    }

    public BlueprintId BlueprintId { get; }

    public BlueprintId? ParentBlueprintId { get; }

    public string ProductName { get; }

    public BlueprintActivityType ActivityType { get; }

    public string ActivityName => ActivityType == BlueprintActivityType.Reaction ? "Reaction" : "Manufacturing";

    public string FacilityName { get; }

    public long RequiredQuantity { get; }

    public int TotalRuns { get; }

    public TimeSpan TotalTime { get; }

    public string TotalTimeText => TotalTime.TotalHours >= 1
        ? $"{TotalTime.TotalHours:N1}h"
        : $"{Math.Max(1, (int)Math.Ceiling(TotalTime.TotalMinutes)):N0}m";

    public int Depth { get; }

    public bool CanEditEfficiency => Depth > 0 && ActivityType == BlueprintActivityType.Manufacturing;

    public string EfficiencyHint => ActivityType == BlueprintActivityType.Reaction
        ? "Reaction formulas always use ME 0 / TE 0"
        : Depth == 0
            ? "Edit the final blueprint in the main inputs"
            : "Component override; recalculates the plan";

    public int MaterialEfficiency
    {
        get => materialEfficiency;
        set
        {
            if (SetProperty(ref materialEfficiency, Math.Clamp(value, 0, 10)))
            {
                NotifyEfficiencyChanged();
            }
        }
    }

    public int TimeEfficiency
    {
        get => timeEfficiency;
        set
        {
            if (SetProperty(ref timeEfficiency, Math.Clamp(value, 0, 20)))
            {
                NotifyEfficiencyChanged();
            }
        }
    }

    public ObservableCollection<ProductionTreeNodeViewModel> Children { get; } = [];

    private void NotifyEfficiencyChanged()
    {
        if (CanEditEfficiency)
        {
            efficiencyChanged(BlueprintId, MaterialEfficiency, TimeEfficiency);
        }
    }
}
