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

        var totalRuns = request.Runs * request.Lines;
        var outputQuantity = blueprint.ProductQuantity * totalRuns;
        var productUnitPrice = productPrice?.Select(request.PriceProfile.ProductPriceSelection) ?? 0m;
        var estimatedItemValue = productUnitPrice * outputQuantity;
        var rootFacility = GetRootFacility(request, blueprint);
        var finalFacility = FacilityFittingCalculator.ApplyBlueprintScope(rootFacility, blueprint);
        var finalEfficiency = GetBlueprintEfficiency(request, blueprint, depth: 0);
        var jobCost = CalculateJobCost(productUnitPrice * blueprint.ProductQuantity * request.Runs, finalFacility) * request.Lines;
        var productionTime = TimeSpan.FromTicks(CalculateProductionTime(blueprint.BaseProductionTime, request.Runs, finalEfficiency.TimeEfficiency, finalFacility.TimeMultiplier).Ticks * request.Lines);
        var productionJobs = Enumerable.Range(0, request.Lines)
            .Select(_ => CreateProductionJob(
                blueprint,
                rootFacility,
                finalFacility,
                blueprint.ProductQuantity * request.Runs,
                request.Runs,
                finalEfficiency,
                0,
                string.Empty,
                null))
            .ToList();
        var buildPlan = await ResolveMaterialsAsync(blueprint, request, materialPriceProfile, rootFacility, request.Runs, request.Lines, 0, [], warnings, cancellationToken);
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
        return CalculateMaterialBreakdown(baseQuantity, runs, 1, materialEfficiency, facilityMaterialMultiplier).QuantityPerJob;
    }

    public static MaterialCalculationBreakdown CalculateMaterialBreakdown(long baseQuantity, int runs, int lines, int materialEfficiency, decimal facilityMaterialMultiplier)
    {
        var blueprintMaterialMultiplier = 1m - materialEfficiency / 100m;
        var rawQuantityPerJob = baseQuantity * runs * blueprintMaterialMultiplier * facilityMaterialMultiplier;
        var roundedQuantityPerJob = Math.Round(rawQuantityPerJob, 2, MidpointRounding.AwayFromZero);
        var quantityPerJob = Math.Max(runs, (long)Math.Ceiling(roundedQuantityPerJob));

        return new MaterialCalculationBreakdown
        {
            BaseQuantity = baseQuantity,
            RunsPerJob = runs,
            Lines = lines,
            MaterialEfficiency = materialEfficiency,
            BlueprintMaterialMultiplier = blueprintMaterialMultiplier,
            FacilityMaterialMultiplier = facilityMaterialMultiplier,
            RawQuantityPerJob = rawQuantityPerJob,
            RoundedQuantityPerJob = roundedQuantityPerJob,
            QuantityPerJob = quantityPerJob,
            TotalQuantity = quantityPerJob * lines
        };
    }

    public static TimeSpan CalculateProductionTime(TimeSpan baseTime, int runs, int timeEfficiency, decimal facilityTimeMultiplier)
    {
        var efficiencyMultiplier = 1m - timeEfficiency / 100m;
        var seconds = (decimal)baseTime.TotalSeconds * runs * efficiencyMultiplier * facilityTimeMultiplier;
        return TimeSpan.FromSeconds((double)Math.Max(1m, seconds));
    }

    private static IReadOnlyList<int> SplitRunsByJobDuration(
        BlueprintActivityType activityType,
        int totalRuns,
        TimeSpan timePerRun,
        ManufacturingRequest request)
    {
        var maxJobHours = activityType == BlueprintActivityType.Reaction
            ? request.MaxReactionJobHours
            : request.MaxManufacturingJobHours;

        if (totalRuns <= 1 || maxJobHours <= 0 || timePerRun <= TimeSpan.Zero)
        {
            return [Math.Max(1, totalRuns)];
        }

        var runsPerJob = (int)Math.Floor(TimeSpan.FromHours((double)maxJobHours).TotalSeconds / timePerRun.TotalSeconds);
        runsPerJob = Math.Clamp(runsPerJob, 1, totalRuns);

        var chunks = new List<int>();
        var remainingRuns = totalRuns;
        while (remainingRuns > 0)
        {
            var chunkRuns = Math.Min(runsPerJob, remainingRuns);
            chunks.Add(chunkRuns);
            remainingRuns -= chunkRuns;
        }

        return chunks;
    }

    private static void ValidateRequest(ManufacturingRequest request)
    {
        if (request.Runs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Runs must be greater than zero.");
        }

        if (request.Lines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Lines must be greater than zero.");
        }

        if (request.MaterialEfficiency is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Material efficiency must be between 0 and 10 for the MVP.");
        }

        if (request.TimeEfficiency is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Time efficiency must be between 0 and 20 for the MVP.");
        }

        ValidateEfficiency(
            request.DefaultComponentMaterialEfficiency,
            request.DefaultComponentTimeEfficiency,
            "Default component efficiency");

        foreach (var (blueprintId, efficiency) in request.BlueprintEfficiencyOverrides)
        {
            ValidateEfficiency(efficiency.MaterialEfficiency, efficiency.TimeEfficiency, $"Blueprint {blueprintId.Value} efficiency");
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
        int lines,
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
        var blueprintEfficiency = GetBlueprintEfficiency(request, blueprint, depth);

        activeProductTypes.Add(blueprint.ProductTypeId);

        foreach (var material in blueprint.Materials)
        {
            var calculation = CalculateMaterialBreakdown(material.Quantity, runs, lines, blueprintEfficiency.MaterialEfficiency, effectiveFacility.MaterialMultiplier);
            var quantity = calculation.TotalQuantity;
            var buyRequirement = await CreateBuyRequirementAsync(material, quantity, calculation, request.PriceProfile.MaterialPriceSelection, materialPriceProfile, warnings, cancellationToken);
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
                    var componentEfficiency = GetBlueprintEfficiency(request, componentBlueprint, depth + 1);
                    var componentRunChunks = SplitRunsByJobDuration(
                        componentBlueprint.ActivityType,
                        componentRuns,
                        CalculateProductionTime(componentBlueprint.BaseProductionTime, 1, componentEfficiency.TimeEfficiency, effectiveComponentFacility.TimeMultiplier),
                        request);
                    var componentPlans = new List<BuildPlan>();

                    foreach (var componentRunChunk in componentRunChunks)
                    {
                        componentPlans.Add(await ResolveMaterialsAsync(
                            componentBlueprint,
                            request,
                            materialPriceProfile,
                            componentFacility,
                            componentRunChunk,
                            1,
                            depth + 1,
                            new HashSet<TypeId>(activeProductTypes),
                            warnings,
                            cancellationToken));
                    }

                    var componentProductPrice = await priceProvider.GetPriceAsync(componentBlueprint.ProductTypeId, materialPriceProfile, cancellationToken);
                    var componentProductUnitPrice = componentProductPrice?.Select(request.PriceProfile.MaterialPriceSelection) ?? buyRequirement.UnitPrice;
                    var componentJobCost = componentRunChunks.Sum(componentRunChunk =>
                        CalculateJobCost(componentProductUnitPrice * componentBlueprint.ProductQuantity * componentRunChunk, effectiveComponentFacility));
                    var componentProductionTime = TimeSpan.FromTicks(componentRunChunks.Sum(componentRunChunk =>
                        CalculateProductionTime(componentBlueprint.BaseProductionTime, componentRunChunk, componentEfficiency.TimeEfficiency, effectiveComponentFacility.TimeMultiplier).Ticks));
                    var buildCost = componentPlans.Sum(plan => plan.MaterialCost + plan.JobCost) + componentJobCost;

                    decisions.AddRange(componentPlans.SelectMany(plan => plan.Decisions));
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
                        materials.AddRange(componentPlans.SelectMany(plan => plan.Materials));
                        jobCost += componentPlans.Sum(plan => plan.JobCost) + componentJobCost;
                        productionTime += TimeSpan.FromTicks(componentPlans.Sum(plan => plan.ProductionTime.Ticks)) + componentProductionTime;
                        jobs.AddRange(componentRunChunks.Select(componentRunChunk =>
                            CreateProductionJob(
                                componentBlueprint,
                                componentFacility,
                                effectiveComponentFacility,
                                componentRunChunk * componentBlueprint.ProductQuantity,
                                componentRunChunk,
                                componentEfficiency,
                                depth + 1,
                                blueprint.ProductName,
                                blueprint.BlueprintId)));
                        jobs.AddRange(componentPlans.SelectMany(plan => plan.Jobs));
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
        MaterialCalculationBreakdown calculation,
        MarketPriceSelection priceSelection,
        PriceProfile materialPriceProfile,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (priceSelection == MarketPriceSelection.InstantBuy && priceProvider is IMarketOrderBookProvider orderBookProvider)
        {
            var effectivePrice = await orderBookProvider.GetEffectivePriceAsync(material.TypeId, materialPriceProfile, priceSelection, quantity, cancellationToken);
            if (effectivePrice is not null)
            {
                if (!effectivePrice.HasEnoughVolume)
                {
                    warnings.Add($"Only {effectivePrice.FilledQuantity:N0} of {quantity:N0} units were available for {material.Name} instant buy pricing.");
                }

                return new MaterialRequirement
                {
                    TypeId = material.TypeId,
                    Name = material.Name,
                    Quantity = quantity,
                    UnitPrice = effectivePrice.UnitPrice,
                    TotalVolume = quantity * material.Volume,
                    Category = material.Category,
                    MissingPrice = false,
                    HasEnoughMarketVolume = effectivePrice.HasEnoughVolume,
                    MarketFilledQuantity = effectivePrice.FilledQuantity,
                    Calculation = calculation
                };
            }
        }

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
            MissingPrice = price is null,
            HasEnoughMarketVolume = true,
            MarketFilledQuantity = quantity,
            Calculation = calculation
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

    private static FacilityProfile GetRootFacility(ManufacturingRequest request, BlueprintDefinition blueprint)
    {
        return blueprint.ActivityType == BlueprintActivityType.Reaction
            ? request.ReactionFacility
            : GetFinalFacility(request);
    }

    private static BlueprintEfficiencySettings GetBlueprintEfficiency(
        ManufacturingRequest request,
        BlueprintDefinition blueprint,
        int depth)
    {
        if (blueprint.ActivityType == BlueprintActivityType.Reaction)
        {
            return new BlueprintEfficiencySettings(0, 0);
        }

        if (depth == 0)
        {
            return new BlueprintEfficiencySettings(request.MaterialEfficiency, request.TimeEfficiency);
        }

        return request.BlueprintEfficiencyOverrides.TryGetValue(blueprint.BlueprintId, out var efficiency)
            ? efficiency
            : new BlueprintEfficiencySettings(
                request.DefaultComponentMaterialEfficiency,
                request.DefaultComponentTimeEfficiency);
    }

    private static void ValidateEfficiency(int materialEfficiency, int timeEfficiency, string name)
    {
        if (materialEfficiency is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(materialEfficiency), $"{name} ME must be between 0 and 10.");
        }

        if (timeEfficiency is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(timeEfficiency), $"{name} TE must be between 0 and 20.");
        }
    }

    private static ProductionJobRequirement CreateProductionJob(
        BlueprintDefinition blueprint,
        FacilityProfile facility,
        FacilityProfile effectiveFacility,
        long requiredQuantity,
        int runs,
        BlueprintEfficiencySettings efficiency,
        int depth,
        string parentProductName,
        BlueprintId? parentBlueprintId)
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
            ParentBlueprintId = parentBlueprintId,
            RequiredQuantity = requiredQuantity,
            OutputQuantityPerRun = blueprint.ProductQuantity,
            TotalRuns = runs,
            TimePerRun = CalculateProductionTime(blueprint.BaseProductionTime, 1, efficiency.TimeEfficiency, effectiveFacility.TimeMultiplier),
            TotalTime = CalculateProductionTime(blueprint.BaseProductionTime, runs, efficiency.TimeEfficiency, effectiveFacility.TimeMultiplier),
            MaterialEfficiency = efficiency.MaterialEfficiency,
            TimeEfficiency = efficiency.TimeEfficiency,
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
