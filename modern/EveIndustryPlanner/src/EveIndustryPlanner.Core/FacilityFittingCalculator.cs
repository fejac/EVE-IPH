namespace EveIndustryPlanner.Core;

public enum FacilitySecurityBand
{
    HighSec,
    LowSec,
    NullSec
}

public enum FacilityServiceRole
{
    Manufacturing,
    Reactions
}

public enum FacilityRigSize
{
    None,
    Medium,
    Large,
    XLarge
}

public sealed record FacilityStructurePreset(
    string Id,
    string Name,
    int RigSlots,
    FacilityRigSize RigSize,
    decimal MaterialMultiplier,
    decimal TimeMultiplier,
    decimal JobCostMultiplier);

public sealed record FacilityRigPreset(
    string Id,
    string Name,
    FacilityRigSize RigSize,
    FacilityServiceRole Role,
    string ScopeName,
    IReadOnlySet<int> ProductGroupIds,
    IReadOnlySet<int> ProductCategoryIds,
    decimal HighSecMaterialReduction,
    decimal LowSecMaterialReduction,
    decimal NullSecMaterialReduction,
    decimal HighSecTimeReduction,
    decimal LowSecTimeReduction,
    decimal NullSecTimeReduction,
    decimal HighSecCostReduction,
    decimal LowSecCostReduction,
    decimal NullSecCostReduction);

public sealed record FacilityFittingResult(
    decimal MaterialMultiplier,
    decimal TimeMultiplier,
    decimal JobCostMultiplier);

public static class FacilityFittingCalculator
{
    public const string NoneRigId = "none";
    private static IReadOnlyList<FacilityRigPreset> rigPresets = CreateDefaultRigPresets();

    public static IReadOnlyList<FacilityStructurePreset> StructurePresets { get; } =
    [
        new("station", "NPC Station / Neutral", 0, FacilityRigSize.None, 1.00m, 1.00m, 1.00m),
        new("raitaru", "Raitaru", 3, FacilityRigSize.Medium, 0.99m, 0.85m, 0.97m),
        new("azbel", "Azbel", 3, FacilityRigSize.Large, 0.99m, 0.80m, 0.96m),
        new("sotiyo", "Sotiyo", 3, FacilityRigSize.XLarge, 0.99m, 0.70m, 0.95m),
        new("athanor", "Athanor", 3, FacilityRigSize.Medium, 1.00m, 0.90m, 1.00m),
        new("tatara", "Tatara", 3, FacilityRigSize.Large, 1.00m, 0.85m, 1.00m)
    ];

    public static IReadOnlyList<FacilityRigPreset> RigPresets => rigPresets;

    public static void UseRigPresets(IReadOnlyList<FacilityRigPreset> presets)
    {
        if (presets.Count == 0)
        {
            return;
        }

        rigPresets = presets.Any(preset => preset.Id == NoneRigId)
            ? presets
            : [CreateEmptyRigPreset(), .. presets];
    }

    private static IReadOnlyList<FacilityRigPreset> CreateDefaultRigPresets()
    {
        return
    [
        CreateEmptyRigPreset(),
        new("mfg-material", "Manufacturing Material Efficiency", FacilityRigSize.Medium, FacilityServiceRole.Manufacturing, "Manufacturing", new HashSet<int>(), new HashSet<int>(), 0.02m, 0.021m, 0.024m, 0m, 0m, 0m, 0m, 0m, 0m),
        new("mfg-time", "Manufacturing Time Efficiency", FacilityRigSize.Medium, FacilityServiceRole.Manufacturing, "Manufacturing", new HashSet<int>(), new HashSet<int>(), 0m, 0m, 0m, 0.20m, 0.21m, 0.24m, 0m, 0m, 0m),
        new("mfg-cost", "Manufacturing Cost Optimization", FacilityRigSize.Medium, FacilityServiceRole.Manufacturing, "Manufacturing", new HashSet<int>(), new HashSet<int>(), 0m, 0m, 0m, 0m, 0m, 0m, 0.02m, 0.021m, 0.024m),
        new("component-material", "Component Material Efficiency", FacilityRigSize.Medium, FacilityServiceRole.Manufacturing, "Components", new HashSet<int> { 334, 913, 964, 1718 }, new HashSet<int>(), 0.02m, 0.021m, 0.024m, 0m, 0m, 0m, 0m, 0m, 0m),
        new("component-time", "Component Time Efficiency", FacilityRigSize.Medium, FacilityServiceRole.Manufacturing, "Components", new HashSet<int> { 334, 913, 964, 1718 }, new HashSet<int>(), 0m, 0m, 0m, 0.20m, 0.21m, 0.24m, 0m, 0m, 0m),
        new("reaction-material", "Reaction Material Efficiency", FacilityRigSize.Medium, FacilityServiceRole.Reactions, "Reactions", new HashSet<int>(), new HashSet<int>(), 0.02m, 0.021m, 0.024m, 0m, 0m, 0m, 0m, 0m, 0m),
        new("reaction-time", "Reaction Time Efficiency", FacilityRigSize.Medium, FacilityServiceRole.Reactions, "Reactions", new HashSet<int>(), new HashSet<int>(), 0m, 0m, 0m, 0.20m, 0.21m, 0.24m, 0m, 0m, 0m)
    ];
    }

    private static FacilityRigPreset CreateEmptyRigPreset()
    {
        return new FacilityRigPreset(NoneRigId, "Empty Slot", FacilityRigSize.None, FacilityServiceRole.Manufacturing, string.Empty, new HashSet<int>(), new HashSet<int>(), 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
    }

    public static FacilityFittingResult Calculate(
        string structureId,
        FacilitySecurityBand securityBand,
        IEnumerable<string> rigIds)
    {
        return Calculate(structureId, securityBand, rigIds, null);
    }

    public static FacilityProfile ApplyBlueprintScope(FacilityProfile facility, BlueprintDefinition blueprint)
    {
        if (!facility.RigIds.Any(id => !string.IsNullOrWhiteSpace(id) && id != NoneRigId))
        {
            return facility;
        }

        var result = Calculate(
            facility.StructureId,
            facility.SecurityBand,
            facility.RigIds,
            blueprint);

        return new FacilityProfile
        {
            Name = facility.Name,
            SolarSystemId = facility.SolarSystemId,
            SolarSystemName = facility.SolarSystemName,
            StructureId = facility.StructureId,
            StructureName = facility.StructureName,
            ServiceRole = facility.ServiceRole,
            ServiceModuleIds = facility.ServiceModuleIds,
            SecurityBand = facility.SecurityBand,
            RigIds = facility.RigIds,
            MaterialMultiplier = result.MaterialMultiplier,
            TimeMultiplier = result.TimeMultiplier,
            JobCostMultiplier = result.JobCostMultiplier,
            SystemCostIndex = facility.SystemCostIndex,
            FacilityTaxRate = facility.FacilityTaxRate
        };
    }

    private static FacilityFittingResult Calculate(
        string structureId,
        FacilitySecurityBand securityBand,
        IEnumerable<string> rigIds,
        BlueprintDefinition? blueprint)
    {
        var structure = FindStructure(structureId);
        var materialMultiplier = structure.MaterialMultiplier;
        var timeMultiplier = structure.TimeMultiplier;
        var costMultiplier = structure.JobCostMultiplier;

        foreach (var rigId in rigIds.Where(id => !string.IsNullOrWhiteSpace(id) && id != NoneRigId))
        {
            var rig = FindRig(rigId);
            if (!AppliesToBlueprint(rig, blueprint))
            {
                continue;
            }

            materialMultiplier *= 1m - GetReduction(
                securityBand,
                rig.HighSecMaterialReduction,
                rig.LowSecMaterialReduction,
                rig.NullSecMaterialReduction);
            timeMultiplier *= 1m - GetReduction(
                securityBand,
                rig.HighSecTimeReduction,
                rig.LowSecTimeReduction,
                rig.NullSecTimeReduction);
            costMultiplier *= 1m - GetReduction(
                securityBand,
                rig.HighSecCostReduction,
                rig.LowSecCostReduction,
                rig.NullSecCostReduction);
        }

        return new FacilityFittingResult(
            Math.Round(materialMultiplier, 6, MidpointRounding.AwayFromZero),
            Math.Round(timeMultiplier, 6, MidpointRounding.AwayFromZero),
            Math.Round(costMultiplier, 6, MidpointRounding.AwayFromZero));
    }

    private static bool AppliesToBlueprint(FacilityRigPreset rig, BlueprintDefinition? blueprint)
    {
        if (blueprint is null)
        {
            return true;
        }

        var blueprintRole = blueprint.ActivityType == BlueprintActivityType.Reaction
            ? FacilityServiceRole.Reactions
            : FacilityServiceRole.Manufacturing;
        if (rig.Role != blueprintRole)
        {
            return false;
        }

        if (rig.ProductGroupIds.Count == 0 && rig.ProductCategoryIds.Count == 0)
        {
            return true;
        }

        return rig.ProductGroupIds.Contains(blueprint.ProductGroupId)
            || rig.ProductCategoryIds.Contains(blueprint.ProductCategoryId);
    }

    private static FacilityStructurePreset FindStructure(string structureId)
    {
        return StructurePresets.FirstOrDefault(preset => preset.Id == structureId)
            ?? StructurePresets.First();
    }

    private static FacilityRigPreset FindRig(string rigId)
    {
        return RigPresets.FirstOrDefault(preset => preset.Id == rigId)
            ?? RigPresets.First();
    }

    private static decimal GetReduction(FacilitySecurityBand securityBand, decimal highSec, decimal lowSec, decimal nullSec)
    {
        return securityBand switch
        {
            FacilitySecurityBand.HighSec => highSec,
            FacilitySecurityBand.LowSec => lowSec,
            FacilitySecurityBand.NullSec => nullSec,
            _ => highSec
        };
    }
}
