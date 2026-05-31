using YamlDotNet.Serialization;

namespace EveIndustryPlanner.Core;

public static class SdeFacilityRigCatalog
{
    private const int HighSecModifierAttributeId = 2355;
    private const int LowSecModifierAttributeId = 2356;
    private const int NullSecModifierAttributeId = 2357;
    private const int EngineeringRigTimeBonusAttributeId = 2593;
    private const int EngineeringRigMaterialBonusAttributeId = 2594;
    private const int EngineeringRigCostBonusAttributeId = 2595;
    private const int ReactionRigTimeBonusAttributeId = 2713;
    private const int ReactionRigMaterialBonusAttributeId = 2714;
    private const int HybridReactionTimeAttributeId = 2715;
    private const int HybridReactionMaterialAttributeId = 2716;
    private const int CompositeReactionTimeAttributeId = 2717;
    private const int CompositeReactionMaterialAttributeId = 2718;
    private const int BiochemicalReactionTimeAttributeId = 2719;
    private const int BiochemicalReactionMaterialAttributeId = 2720;

    public static IReadOnlyList<FacilityRigPreset> Load(string sdeDirectory)
    {
        var typePath = Path.Combine(sdeDirectory, "types.yaml");
        var groupPath = Path.Combine(sdeDirectory, "groups.yaml");
        var dogmaPath = Path.Combine(sdeDirectory, "typeDogma.yaml");
        var dogmaEffectsPath = Path.Combine(sdeDirectory, "dogmaEffects.yaml");
        if (!File.Exists(typePath) || !File.Exists(groupPath) || !File.Exists(dogmaPath) || !File.Exists(dogmaEffectsPath))
        {
            return FacilityFittingCalculator.RigPresets;
        }

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        var groups = deserializer.Deserialize<Dictionary<int, SdeGroup>>(File.ReadAllText(groupPath));
        var types = deserializer.Deserialize<Dictionary<long, SdeType>>(File.ReadAllText(typePath));
        var dogma = deserializer.Deserialize<Dictionary<long, SdeTypeDogma>>(File.ReadAllText(dogmaPath));
        var dogmaEffects = deserializer.Deserialize<Dictionary<int, SdeDogmaEffect>>(File.ReadAllText(dogmaEffectsPath));

        var rigs = types
            .Select(pair => ToRigPreset(pair.Key, pair.Value, groups, dogma, dogmaEffects))
            .Where(preset => preset is not null)
            .Select(preset => preset!)
            .OrderBy(preset => preset.Role)
            .ThenBy(preset => preset.Name)
            .ToList();

        return rigs.Count == 0
            ? FacilityFittingCalculator.RigPresets
            : [new FacilityRigPreset(FacilityFittingCalculator.NoneRigId, "Empty Slot", FacilityRigSize.None, FacilityServiceRole.Manufacturing, string.Empty, new HashSet<int>(), new HashSet<int>(), 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m), .. rigs];
    }

    private static FacilityRigPreset? ToRigPreset(
        long typeId,
        SdeType type,
        IReadOnlyDictionary<int, SdeGroup> groups,
        IReadOnlyDictionary<long, SdeTypeDogma> dogma,
        IReadOnlyDictionary<int, SdeDogmaEffect> dogmaEffects)
    {
        var typeName = GetEnglishName(type.Name);
        if (!type.Published
            || !typeName.StartsWith("Standup ", StringComparison.Ordinal)
            || typeName.EndsWith(" Blueprint", StringComparison.Ordinal)
            || !groups.TryGetValue(type.GroupId, out var group))
        {
            return null;
        }

        var groupName = GetEnglishName(group.Name);
        var isManufacturingRig = groupName.Contains("Engineering Rig", StringComparison.Ordinal);
        var isReactionRig = groupName.Contains("Reactor Rig", StringComparison.Ordinal);
        if (!isManufacturingRig && !isReactionRig)
        {
            return null;
        }

        if (!dogma.TryGetValue(typeId, out var typeDogma))
        {
            return null;
        }

        var attributes = typeDogma.DogmaAttributes.ToDictionary(attribute => attribute.AttributeId, attribute => attribute.Value);
        var highSecModifier = GetAttribute(attributes, HighSecModifierAttributeId, 1m);
        var lowSecModifier = GetAttribute(attributes, LowSecModifierAttributeId, 1m);
        var nullSecModifier = GetAttribute(attributes, NullSecModifierAttributeId, 1m);
        var role = isReactionRig ? FacilityServiceRole.Reactions : FacilityServiceRole.Manufacturing;
        var materialBonus = GetMaterialBonus(attributes, role);
        var timeBonus = GetTimeBonus(attributes, role);
        var costBonus = Math.Abs(GetAttribute(attributes, EngineeringRigCostBonusAttributeId, 0m)) / 100m;
        var scope = InferScope(typeName, role, typeDogma, dogmaEffects);
        var rigSize = InferRigSize(typeName);

        if (materialBonus == 0m && timeBonus == 0m && costBonus == 0m)
        {
            return null;
        }

        return new FacilityRigPreset(
            typeId.ToString(),
            typeName,
            rigSize,
            role,
            scope.Name,
            scope.ProductGroupIds,
            scope.ProductCategoryIds,
            materialBonus * highSecModifier,
            materialBonus * lowSecModifier,
            materialBonus * nullSecModifier,
            timeBonus * highSecModifier,
            timeBonus * lowSecModifier,
            timeBonus * nullSecModifier,
            costBonus * highSecModifier,
            costBonus * lowSecModifier,
            costBonus * nullSecModifier);
    }

    private static FacilityRigSize InferRigSize(string typeName)
    {
        if (typeName.Contains(" XL-Set ", StringComparison.Ordinal))
        {
            return FacilityRigSize.XLarge;
        }

        if (typeName.Contains(" L-Set ", StringComparison.Ordinal))
        {
            return FacilityRigSize.Large;
        }

        if (typeName.Contains(" M-Set ", StringComparison.Ordinal))
        {
            return FacilityRigSize.Medium;
        }

        return FacilityRigSize.None;
    }

    private static RigScope InferScope(
        string typeName,
        FacilityServiceRole role,
        SdeTypeDogma typeDogma,
        IReadOnlyDictionary<int, SdeDogmaEffect> dogmaEffects)
    {
        var scopes = typeDogma.DogmaEffects
            .Select(effect => dogmaEffects.TryGetValue(effect.EffectId, out var dogmaEffect) ? dogmaEffect : null)
            .Where(effect => effect is not null)
            .SelectMany(effect => effect!.ModifierInfo)
            .Where(modifier => IsRigBonusAttribute(modifier.ModifyingAttributeId))
            .Select(modifier => FindLegacyScope(modifier.ModifiedAttributeId))
            .Where(scope => scope is not null)
            .Select(scope => scope!)
            .ToList();

        if (scopes.Count == 0)
        {
            return InferScopeFromName(typeName, role);
        }

        var scopeNames = scopes
            .Select(scope => scope.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var groupIds = scopes.SelectMany(scope => scope.ProductGroupIds).ToHashSet();
        var categoryIds = scopes.SelectMany(scope => scope.ProductCategoryIds).ToHashSet();

        return new RigScope(string.Join(", ", scopeNames), groupIds, categoryIds);
    }

    private static bool IsRigBonusAttribute(int attributeId)
    {
        return attributeId is EngineeringRigTimeBonusAttributeId
            or EngineeringRigMaterialBonusAttributeId
            or EngineeringRigCostBonusAttributeId
            or ReactionRigTimeBonusAttributeId
            or ReactionRigMaterialBonusAttributeId
            or HybridReactionTimeAttributeId
            or HybridReactionMaterialAttributeId
            or CompositeReactionTimeAttributeId
            or CompositeReactionMaterialAttributeId
            or BiochemicalReactionTimeAttributeId
            or BiochemicalReactionMaterialAttributeId
            or 2653;
    }

    private static RigScope? FindLegacyScope(int modifiedAttributeId)
    {
        return modifiedAttributeId switch
        {
            2538 or 2539 => new RigScope("Equipment", Set(), Set(7, 20, 22, 32)),
            2540 or 2541 => new RigScope("Ammunition / Charges", Set(), Set(8)),
            2542 or 2543 => new RigScope("Drones / Fighters", Set(), Set(18, 87)),
            2544 or 2545 => new RigScope("Basic Small Ships", Set(25, 31, 420), Set()),
            2546 or 2547 => new RigScope("Basic Medium Ships", Set(26, 28, 419, 463, 1201), Set()),
            2548 or 2549 => new RigScope("Basic Large Ships", Set(27, 513, 941), Set()),
            2550 or 2551 => new RigScope("Advanced Small Ships", Set(324, 541, 830, 831, 834, 893, 1305, 1527, 1534), Set()),
            2552 or 2553 => new RigScope("Advanced Medium Ships", Set(358, 380, 540, 543, 832, 833, 894, 906, 963, 1202), Set(32)),
            2555 or 2556 => new RigScope("Advanced Large Ships", Set(898, 900, 902), Set()),
            2557 or 2558 => new RigScope("Advanced Components", Set(334, 332, 716, 964), Set()),
            2559 or 2560 => new RigScope("Basic Capital Components", Set(873), Set()),
            2561 or 2562 => new RigScope("Structures", Set(536, 1136, 1321), Set(23, 65, 66)),
            2575 or 2576 => new RigScope("Capital Ships", Set(30, 485, 547, 659, 883, 902, 1538), Set()),
            2591 or 2592 => new RigScope("All Ships", Set(), Set(6)),
            2658 or 2659 => new RigScope("Advanced Capital Components", Set(913), Set()),
            _ => null
        };
    }

    private static RigScope InferScopeFromName(string typeName, FacilityServiceRole role)
    {
        if (role == FacilityServiceRole.Reactions)
        {
            if (typeName.Contains("Hybrid", StringComparison.OrdinalIgnoreCase))
            {
                return new RigScope("Hybrid Reactions", Set(), Set());
            }

            if (typeName.Contains("Composite", StringComparison.OrdinalIgnoreCase))
            {
                return new RigScope("Composite Reactions", Set(), Set());
            }

            if (typeName.Contains("Biochemical", StringComparison.OrdinalIgnoreCase))
            {
                return new RigScope("Biochemical Reactions", Set(), Set());
            }

            return new RigScope("Reactions", Set(), Set());
        }

        if (typeName.Contains("Advanced Component", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Advanced Components", Set(334, 913, 964, 1718), Set());
        }

        if (typeName.Contains("Component", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Components", Set(334, 536, 873, 913, 964, 1718), Set());
        }

        if (typeName.Contains("Advanced Large Ship", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Advanced Large Ships", Set(898, 900), Set());
        }

        if (typeName.Contains("Basic Large Ship", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Basic Large Ships", Set(27, 513, 883, 941), Set());
        }

        if (typeName.Contains("Advanced Medium Ship", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Advanced Medium Ships", Set(358, 540, 832, 833, 894, 906, 963), Set());
        }

        if (typeName.Contains("Basic Medium Ship", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Basic Medium Ships", Set(26, 28, 419, 463, 1201), Set());
        }

        if (typeName.Contains("Advanced Small Ship", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Advanced Small Ships", Set(324, 541, 830, 834, 893, 1283, 1305, 1527, 1534), Set());
        }

        if (typeName.Contains("Basic Small Ship", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Basic Small Ships", Set(25, 31, 420), Set());
        }

        if (typeName.Contains("Capital Ship", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Capital Ships", Set(30, 485, 547, 659, 883, 902), Set());
        }

        if (typeName.Contains("Ammunition", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Ammunition / Charges", Set(), Set(8));
        }

        if (typeName.Contains("Drone", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Fighter", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Drones / Fighters", Set(), Set(18, 87));
        }

        if (typeName.Contains("Equipment", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Equipment", Set(), Set(7, 20, 22, 32));
        }

        if (typeName.Contains("Structure", StringComparison.OrdinalIgnoreCase))
        {
            return new RigScope("Structures", Set(), Set(65, 66));
        }

        return new RigScope("Manufacturing", Set(), Set());
    }

    private static IReadOnlySet<int> Set(params int[] ids)
    {
        return ids.Length == 0 ? new HashSet<int>() : new HashSet<int>(ids);
    }

    private static decimal GetMaterialBonus(IReadOnlyDictionary<int, decimal> attributes, FacilityServiceRole role)
    {
        if (role == FacilityServiceRole.Manufacturing)
        {
            return Math.Abs(GetAttribute(attributes, EngineeringRigMaterialBonusAttributeId, 0m)) / 100m;
        }

        return Math.Abs(FirstNonZero(
            GetAttribute(attributes, ReactionRigMaterialBonusAttributeId, 0m),
            GetAttribute(attributes, HybridReactionMaterialAttributeId, 0m),
            GetAttribute(attributes, CompositeReactionMaterialAttributeId, 0m),
            GetAttribute(attributes, BiochemicalReactionMaterialAttributeId, 0m))) / 100m;
    }

    private static decimal GetTimeBonus(IReadOnlyDictionary<int, decimal> attributes, FacilityServiceRole role)
    {
        if (role == FacilityServiceRole.Manufacturing)
        {
            return Math.Abs(GetAttribute(attributes, EngineeringRigTimeBonusAttributeId, 0m)) / 100m;
        }

        return Math.Abs(FirstNonZero(
            GetAttribute(attributes, ReactionRigTimeBonusAttributeId, 0m),
            GetAttribute(attributes, HybridReactionTimeAttributeId, 0m),
            GetAttribute(attributes, CompositeReactionTimeAttributeId, 0m),
            GetAttribute(attributes, BiochemicalReactionTimeAttributeId, 0m))) / 100m;
    }

    private static decimal GetAttribute(IReadOnlyDictionary<int, decimal> attributes, int attributeId, decimal defaultValue)
    {
        return attributes.TryGetValue(attributeId, out var value) ? value : defaultValue;
    }

    private static decimal FirstNonZero(params decimal[] values)
    {
        return values.FirstOrDefault(value => value != 0m);
    }

    private static string GetEnglishName(Dictionary<string, string> names)
    {
        return names.TryGetValue("en", out var name) ? name : string.Empty;
    }

    private sealed class SdeType
    {
        [YamlMember(Alias = "name")]
        public Dictionary<string, string> Name { get; init; } = [];

        [YamlMember(Alias = "groupID")]
        public int GroupId { get; init; }

        [YamlMember(Alias = "published")]
        public bool Published { get; init; }
    }

    private sealed class SdeGroup
    {
        [YamlMember(Alias = "name")]
        public Dictionary<string, string> Name { get; init; } = [];
    }

    private sealed class SdeTypeDogma
    {
        [YamlMember(Alias = "dogmaAttributes")]
        public List<SdeDogmaAttribute> DogmaAttributes { get; init; } = [];

        [YamlMember(Alias = "dogmaEffects")]
        public List<SdeTypeDogmaEffect> DogmaEffects { get; init; } = [];
    }

    private sealed class SdeDogmaAttribute
    {
        [YamlMember(Alias = "attributeID")]
        public int AttributeId { get; init; }

        [YamlMember(Alias = "value")]
        public double ValueRaw { get; init; }

        public decimal Value => Convert.ToDecimal(ValueRaw);
    }

    private sealed class SdeTypeDogmaEffect
    {
        [YamlMember(Alias = "effectID")]
        public int EffectId { get; init; }
    }

    private sealed class SdeDogmaEffect
    {
        [YamlMember(Alias = "modifierInfo")]
        public List<SdeDogmaEffectModifier> ModifierInfo { get; init; } = [];
    }

    private sealed class SdeDogmaEffectModifier
    {
        [YamlMember(Alias = "modifiedAttributeID")]
        public int ModifiedAttributeId { get; init; }

        [YamlMember(Alias = "modifyingAttributeID")]
        public int ModifyingAttributeId { get; init; }
    }

    private sealed record RigScope(string Name, IReadOnlySet<int> ProductGroupIds, IReadOnlySet<int> ProductCategoryIds);
}
