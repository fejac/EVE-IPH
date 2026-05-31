using EveIndustryPlanner.Core;

namespace EveIndustryPlanner.Core.Tests;

public sealed class FacilityFittingCalculatorTests
{
    [Fact]
    public void Calculate_AppliesStructureAndRigMultipliers()
    {
        var result = FacilityFittingCalculator.Calculate(
            "raitaru",
            FacilitySecurityBand.HighSec,
            ["mfg-material", "mfg-time", "mfg-cost"]);

        Assert.Equal(0.9702m, result.MaterialMultiplier);
        Assert.Equal(0.6800m, result.TimeMultiplier);
        Assert.Equal(0.9506m, result.JobCostMultiplier);
    }

    [Fact]
    public void Calculate_UsesNullSecRigBonuses()
    {
        var highSec = FacilityFittingCalculator.Calculate(
            "station",
            FacilitySecurityBand.HighSec,
            ["mfg-material"]);
        var nullSec = FacilityFittingCalculator.Calculate(
            "station",
            FacilitySecurityBand.NullSec,
            ["mfg-material"]);

        Assert.True(nullSec.MaterialMultiplier < highSec.MaterialMultiplier);
    }

    [Fact]
    public void Calculate_IgnoresUnknownRigIds()
    {
        var result = FacilityFittingCalculator.Calculate(
            "station",
            FacilitySecurityBand.HighSec,
            ["unknown"]);

        Assert.Equal(1m, result.MaterialMultiplier);
        Assert.Equal(1m, result.TimeMultiplier);
        Assert.Equal(1m, result.JobCostMultiplier);
    }

    [Fact]
    public void ApplyBlueprintScope_OnlyUsesRigsMatchingProductScope()
    {
        var facility = new FacilityProfile
        {
            StructureId = "station",
            SecurityBand = FacilitySecurityBand.HighSec,
            RigIds = ["component-material"]
        };

        var componentBlueprint = new BlueprintDefinition
        {
            ActivityType = BlueprintActivityType.Manufacturing,
            ProductGroupId = 334,
            ProductCategoryId = 17
        };
        var shipBlueprint = new BlueprintDefinition
        {
            ActivityType = BlueprintActivityType.Manufacturing,
            ProductGroupId = 27,
            ProductCategoryId = 6
        };

        var componentFacility = FacilityFittingCalculator.ApplyBlueprintScope(facility, componentBlueprint);
        var shipFacility = FacilityFittingCalculator.ApplyBlueprintScope(facility, shipBlueprint);

        Assert.Equal(0.98m, componentFacility.MaterialMultiplier);
        Assert.Equal(1m, shipFacility.MaterialMultiplier);
    }

    [Fact]
    public void StructurePresets_ExposeLegacyRigSizes()
    {
        Assert.Equal(FacilityRigSize.None, FacilityFittingCalculator.StructurePresets.First(preset => preset.Id == "station").RigSize);
        Assert.Equal(FacilityRigSize.Medium, FacilityFittingCalculator.StructurePresets.First(preset => preset.Id == "raitaru").RigSize);
        Assert.Equal(FacilityRigSize.Medium, FacilityFittingCalculator.StructurePresets.First(preset => preset.Id == "athanor").RigSize);
        Assert.Equal(FacilityRigSize.Large, FacilityFittingCalculator.StructurePresets.First(preset => preset.Id == "azbel").RigSize);
        Assert.Equal(FacilityRigSize.Large, FacilityFittingCalculator.StructurePresets.First(preset => preset.Id == "tatara").RigSize);
        Assert.Equal(FacilityRigSize.XLarge, FacilityFittingCalculator.StructurePresets.First(preset => preset.Id == "sotiyo").RigSize);
    }

    [Fact]
    public void SdeFacilityRigCatalog_LoadsRealStandupRigs()
    {
        var sdeDirectory = FindSdeDirectory();
        if (sdeDirectory is null)
        {
            return;
        }

        var rigs = SdeFacilityRigCatalog.Load(sdeDirectory);
        var mediumShipRig = rigs.FirstOrDefault(rig => rig.Name == "Standup M-Set Basic Medium Ship Manufacturing Material Efficiency I");
        var advancedLargeShipRig = rigs.FirstOrDefault(rig => rig.Name == "Standup M-Set Advanced Large Ship Manufacturing Material Efficiency I");

        Assert.NotNull(mediumShipRig);
        Assert.NotNull(advancedLargeShipRig);
        Assert.True(rigs.Count > 50);
        Assert.Equal("37146", mediumShipRig.Id);
        Assert.Equal(FacilityRigSize.Medium, mediumShipRig.RigSize);
        Assert.Equal(FacilityRigSize.Medium, advancedLargeShipRig.RigSize);
        Assert.Equal(FacilityServiceRole.Manufacturing, mediumShipRig.Role);
        Assert.Equal("Basic Medium Ships", mediumShipRig.ScopeName);
        Assert.Equal(0.02m, mediumShipRig.HighSecMaterialReduction);
        Assert.True(mediumShipRig.NullSecMaterialReduction > mediumShipRig.HighSecMaterialReduction);
    }

    private static string? FindSdeDirectory()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "sde")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "src", "sde")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "sde"))
        };

        return candidates.FirstOrDefault(SdeIndustryDataProvider.IsAvailable);
    }
}
