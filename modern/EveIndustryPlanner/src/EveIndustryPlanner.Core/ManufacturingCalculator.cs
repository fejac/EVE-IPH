namespace EveIndustryPlanner.Core;

public sealed class ManufacturingCalculator(
    IBlueprintRepository blueprintRepository,
    IMarketPriceProvider priceProvider) : IManufacturingCalculator
{
    public async Task<ManufacturingResult> CalculateAsync(ManufacturingRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var blueprint = await blueprintRepository.GetBlueprintAsync(request.BlueprintId, cancellationToken)
            ?? throw new InvalidOperationException($"Blueprint {request.BlueprintId.Value} was not found.");

        var warnings = new List<string>();
        var materialPriceProfile = request.PriceProfile.ForMaterialMarket();
        var productPriceProfile = request.PriceProfile.ForProductMarket();

        var productPrice = await priceProvider.GetPriceAsync(blueprint.ProductTypeId, productPriceProfile, cancellationToken);
        if (productPrice is null)
        {
            warnings.Add($"Missing market price for {blueprint.ProductName}.");
        }

        var outputQuantity = blueprint.ProductQuantity * request.Runs;
        var productUnitPrice = productPrice?.Select(request.PriceProfile.ProductPriceSelection) ?? 0m;
        var estimatedItemValue = productUnitPrice * outputQuantity;
        var finalFacility = FacilityFittingCalculator.ApplyBlueprintScope(GetFinalFacility(request), blueprint);
        var jobCost = CalculateJobCost(estimatedItemValue, finalFacility);
        var productionTime = CalculateProductionTime(blueprint.BaseProductionTime, request.Runs, request.TimeEfficiency, finalFacility.TimeMultiplier);
        var productionJobs = new List<ProductionJobRequirement>
        {
            CreateProductionJob(blueprint, GetFinalFacility(request), finalFacility, outputQuantity, request.Runs, request.TimeEfficiency, 0, string.Empty)
        };
        var buildPlan = await ResolveMaterialsAsync(blueprint, request, materialPriceProfile, GetFinalFacility(request), request.Runs, 0, [], warnings, cancellationToken);
        productionJobs.AddRange(buildPlan.Jobs);
        var materialCost = buildPlan.MaterialCost;
        var totalJobCost = jobCost + buildPlan.JobCost;
        var totalProductionTime = productionTime + buildPlan.ProductionTime;
        var totalCost = materialCost + totalJobCost + request.AdditionalCosts;
        var revenue = productUnitPrice * outputQuantity;
        var profit = revenue - totalCost;
        var iskPerHour = totalProductionTime.TotalHours <= 0 ? 0m : profit / (decimal)totalProductionTime.TotalHours;

        return new ManufacturingResult
        {
            Blueprint = blueprint,
            Materials = buildPlan.Materials,
            BuildBuyDecisions = buildPlan.Decisions,
            ProductionJobs = productionJobs,
            Warnings = warnings,
            OutputQuantity = outputQuantity,
            MaterialCost = materialCost,
            JobCost = totalJobCost,
            FinalJobCost = jobCost,
            BuildJobCost = buildPlan.JobCost,
            AdditionalCosts = request.AdditionalCosts,
            EstimatedRevenue = revenue,
            TotalProductionTime = totalProductionTime,
            FinalProductionTime = productionTime,
            BuildProductionTime = buildPlan.ProductionTime,
            FinalFacilityMaterialMultiplier = finalFacility.MaterialMultiplier,
            FinalFacilityTimeMultiplier = finalFacility.TimeMultiplier,
            FinalFacilityJobCostMultiplier = finalFacility.JobCostMultiplier,
            FinalFacilitySystemCostIndex = finalFacility.SystemCostIndex,
            FinalFacilityTaxRate = finalFacility.FacilityTaxRate,
            IskPerHour = iskPerHour
        };
    }

    public static long CalculateMaterialQuantity(long baseQuantity, int runs, int materialEfficiency, decimal facilityMaterialMultiplier)
    {
        var efficiencyMultiplier = 1m - materialEfficiency / 100m;
        var quantity = baseQuantity * runs * efficiencyMultiplier * facilityMaterialMultiplier;
        return Math.Max(1, (long)Math.Ceiling(quantity));
    }

    public static TimeSpan CalculateProductionTime(TimeSpan baseTime, int runs, int timeEfficiency, decimal facilityTimeMultiplier)
    {
        var efficiencyMultiplier = 1m - timeEfficiency / 100m;
        var seconds = (decimal)baseTime.TotalSeconds * runs * efficiencyMultiplier * facilityTimeMultiplier;
        return TimeSpan.FromSeconds((double)Math.Max(1m, seconds));
    }

    private static void ValidateRequest(ManufacturingRequest request)
    {
        if (request.Runs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Runs must be greater than zero.");
        }

        if (request.MaterialEfficiency is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Material efficiency must be between 0 and 10 for the MVP.");
        }

        if (request.TimeEfficiency is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Time efficiency must be between 0 and 20 for the MVP.");
        }

        if (request.AdditionalCosts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Additional costs cannot be negative.");
        }

        if (request.MaxBuildBuyDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Build/buy depth cannot be negative.");
        }
    }

    private async Task<BuildPlan> ResolveMaterialsAsync(
        BlueprintDefinition blueprint,
        ManufacturingRequest request,
        PriceProfile materialPriceProfile,
        FacilityProfile facility,
        int runs,
        int depth,
        HashSet<TypeId> activeProductTypes,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var materials = new List<MaterialRequirement>();
        var decisions = new List<BuildBuyDecision>();
        var jobCost = 0m;
        var productionTime = TimeSpan.Zero;
        var jobs = new List<ProductionJobRequirement>();
        var effectiveFacility = FacilityFittingCalculator.ApplyBlueprintScope(facility, blueprint);

        activeProductTypes.Add(blueprint.ProductTypeId);

        foreach (var material in blueprint.Materials)
        {
            var quantity = CalculateMaterialQuantity(material.Quantity, runs, request.MaterialEfficiency, effectiveFacility.MaterialMultiplier);
            var buyRequirement = await CreateBuyRequirementAsync(material, quantity, request.PriceProfile.MaterialPriceSelection, materialPriceProfile, warnings, cancellationToken);
            var buyCost = buyRequirement.TotalPrice;

            if (request.EnableBuildBuy && depth < request.MaxBuildBuyDepth)
            {
                var componentBlueprint = await blueprintRepository.GetBlueprintByProductTypeAsync(material.TypeId, cancellationToken);
                if (componentBlueprint is not null
                    && IsBuildAllowed(componentBlueprint, request.BuildBuyDepth)
                    && !activeProductTypes.Contains(componentBlueprint.ProductTypeId))
                {
                    var componentRuns = (int)Math.Ceiling(quantity / (decimal)componentBlueprint.ProductQuantity);
                    var componentFacility = GetFacilityForBlueprint(request, componentBlueprint, material);
                    var effectiveComponentFacility = FacilityFittingCalculator.ApplyBlueprintScope(componentFacility, componentBlueprint);
                    var componentPlan = await ResolveMaterialsAsync(
                        componentBlueprint,
                        request,
                        materialPriceProfile,
                        componentFacility,
                        componentRuns,
                        depth + 1,
                        new HashSet<TypeId>(activeProductTypes),
                        warnings,
                        cancellationToken);

                    var componentOutputQuantity = componentRuns * componentBlueprint.ProductQuantity;
                    var componentProductPrice = await priceProvider.GetPriceAsync(componentBlueprint.ProductTypeId, materialPriceProfile, cancellationToken);
                    var componentProductUnitPrice = componentProductPrice?.Select(request.PriceProfile.MaterialPriceSelection) ?? buyRequirement.UnitPrice;
                    var componentJobCost = CalculateJobCost(componentProductUnitPrice * componentOutputQuantity, effectiveComponentFacility);
                    var componentProductionTime = CalculateProductionTime(componentBlueprint.BaseProductionTime, componentRuns, request.TimeEfficiency, effectiveComponentFacility.TimeMultiplier);
                    var buildCost = componentPlan.MaterialCost + componentPlan.JobCost + componentJobCost;

                    decisions.AddRange(componentPlan.Decisions);
                    decisions.Add(new BuildBuyDecision
                    {
                        TypeId = material.TypeId,
                        Name = material.Name,
                        FacilityName = componentFacility.Name,
                        RequiredQuantity = quantity,
                        Built = buildCost < buyCost,
                        BuyCost = buyCost,
                        BuildCost = buildCost,
                        ActivityType = componentBlueprint.ActivityType,
                        Depth = depth + 1
                    });

                    if (buildCost < buyCost)
                    {
                        materials.AddRange(componentPlan.Materials);
                        jobCost += componentPlan.JobCost + componentJobCost;
                        productionTime += componentPlan.ProductionTime + componentProductionTime;
                        jobs.Add(CreateProductionJob(componentBlueprint, componentFacility, effectiveComponentFacility, quantity, componentRuns, request.TimeEfficiency, depth + 1, blueprint.ProductName));
                        jobs.AddRange(componentPlan.Jobs);
                        continue;
                    }
                }
            }

            materials.Add(buyRequirement);
        }

        return new BuildPlan(materials, decisions, jobs, materials.Sum(material => material.TotalPrice), jobCost, productionTime);
    }

    private async Task<MaterialRequirement> CreateBuyRequirementAsync(
        BlueprintMaterial material,
        long quantity,
        MarketPriceSelection priceSelection,
        PriceProfile materialPriceProfile,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var price = await priceProvider.GetPriceAsync(material.TypeId, materialPriceProfile, cancellationToken);
        var unitPrice = price?.Select(priceSelection) ?? 0m;

        if (price is null)
        {
            warnings.Add($"Missing market price for {material.Name}.");
        }

        return new MaterialRequirement
        {
            TypeId = material.TypeId,
            Name = material.Name,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TotalVolume = quantity * material.Volume,
            Category = material.Category,
            MissingPrice = price is null
        };
    }

    private static bool IsBuildAllowed(BlueprintDefinition blueprint, BuildBuyDepth depth)
    {
        return depth switch
        {
            BuildBuyDepth.DirectMaterialsOnly => false,
            BuildBuyDepth.BuildManufacturingComponents => blueprint.ActivityType == BlueprintActivityType.Manufacturing,
            BuildBuyDepth.BuildManufacturingAndReactions => true,
            _ => false
        };
    }

    private static FacilityProfile GetFinalFacility(ManufacturingRequest request)
    {
        return request.FinalProductFacility.Name.Length == 0 ? request.Facility : request.FinalProductFacility;
    }

    private static FacilityProfile GetFacilityForBlueprint(ManufacturingRequest request, BlueprintDefinition blueprint, BlueprintMaterial parentMaterial)
    {
        if (blueprint.ActivityType == BlueprintActivityType.Reaction)
        {
            return request.ReactionFacility;
        }

        if (IsT1BaseItemForT2(parentMaterial))
        {
            return GetFinalFacility(request);
        }

        return request.ComponentFacility;
    }

    private static bool IsT1BaseItemForT2(BlueprintMaterial material)
    {
        return material.CategoryId is 6 or 7 or 8 or 18;
    }

    private static decimal CalculateJobCost(decimal estimatedItemValue, FacilityProfile facility)
    {
        var effectiveRate = facility.SystemCostIndex * facility.JobCostMultiplier + facility.FacilityTaxRate;
        return Math.Round(estimatedItemValue * effectiveRate, 2, MidpointRounding.AwayFromZero);
    }

    private static ProductionJobRequirement CreateProductionJob(
        BlueprintDefinition blueprint,
        FacilityProfile facility,
        FacilityProfile effectiveFacility,
        long requiredQuantity,
        int runs,
        int timeEfficiency,
        int depth,
        string parentProductName)
    {
        return new ProductionJobRequirement
        {
            BlueprintId = blueprint.BlueprintId,
            BlueprintName = blueprint.BlueprintName,
            ProductTypeId = blueprint.ProductTypeId,
            ProductName = blueprint.ProductName,
            ActivityType = blueprint.ActivityType,
            FacilityName = facility.Name,
            ParentProductName = parentProductName,
            RequiredQuantity = requiredQuantity,
            OutputQuantityPerRun = blueprint.ProductQuantity,
            TotalRuns = runs,
            TimePerRun = CalculateProductionTime(blueprint.BaseProductionTime, 1, timeEfficiency, effectiveFacility.TimeMultiplier),
            TotalTime = CalculateProductionTime(blueprint.BaseProductionTime, runs, timeEfficiency, effectiveFacility.TimeMultiplier),
            Depth = depth
        };
    }

    private sealed record BuildPlan(
        IReadOnlyList<MaterialRequirement> Materials,
        IReadOnlyList<BuildBuyDecision> Decisions,
        IReadOnlyList<ProductionJobRequirement> Jobs,
        decimal MaterialCost,
        decimal JobCost,
        TimeSpan ProductionTime);
}
