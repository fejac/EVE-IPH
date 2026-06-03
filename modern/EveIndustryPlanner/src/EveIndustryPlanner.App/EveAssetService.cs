using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EveIndustryPlanner.App;

public sealed class EveAssetService(Func<string> pocketBaseUrlProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient httpClient = new();
    private readonly Dictionary<long, EveAccessToken> accessTokenCache = [];

    public async Task<IReadOnlyList<CachedEveAsset>> LoadAllCharacterAssetsAsync(
        IReadOnlyList<SavedCharacterAccount> accounts,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        var cacheFilePath = GetAssetCacheFilePath();
        if (!refresh)
        {
            var cached = LoadCachedAssets(cacheFilePath);
            if (cached.Count > 0)
            {
                var shouldRefreshForCorporationAssets = accounts.Any(account => account.Scopes.Contains("esi-assets.read_corporation_assets.v1", StringComparer.Ordinal))
                    && cached.All(asset => !asset.IsCorporationAsset);

                if (!shouldRefreshForCorporationAssets && cached.Any(asset => asset.LocationName.StartsWith("Location ", StringComparison.OrdinalIgnoreCase)
                    || asset.LocationName.StartsWith("Unknown Location ", StringComparison.OrdinalIgnoreCase)
                    || asset.LocationName.StartsWith("item ", StringComparison.OrdinalIgnoreCase)
                    || asset.LocationName.StartsWith("other ", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(asset.RootLocationName)
                    || string.IsNullOrWhiteSpace(asset.SolarSystemName)))
                {
                    await PopulateNamesAsync(cached, accounts, cancellationToken).ConfigureAwait(false);
                    SaveCachedAssets(cacheFilePath, cached);
                }

                if (!shouldRefreshForCorporationAssets)
                {
                    return cached;
                }
            }
        }

        var assets = new List<CachedEveAsset>();
        foreach (var account in accounts.Where(account => account.Scopes.Contains("esi-assets.read_assets.v1", StringComparer.Ordinal)))
        {
            var tokenAccount = account;
            var characterAssets = await FetchCharacterAssetsAsync(tokenAccount, cancellationToken).ConfigureAwait(false);

            assets.AddRange(characterAssets.Select(asset => new CachedEveAsset
            {
                CharacterId = account.CharacterId,
                CharacterName = account.CharacterName,
                ItemId = asset.ItemId,
                TypeId = asset.TypeId,
                LocationId = asset.LocationId,
                LocationFlag = asset.LocationFlag,
                LocationType = asset.LocationType,
                Quantity = asset.Quantity,
                IsSingleton = asset.IsSingleton,
                IsBlueprintCopy = asset.IsBlueprintCopy,
                CachedAt = DateTimeOffset.UtcNow
            }));
        }

        var loadedCorporations = new HashSet<long>();
        foreach (var account in accounts.Where(account => account.Scopes.Contains("esi-assets.read_corporation_assets.v1", StringComparer.Ordinal)))
        {
            var corporationId = await FetchCharacterCorporationIdAsync(account.CharacterId, cancellationToken).ConfigureAwait(false);
            if (corporationId <= 0 || !loadedCorporations.Add(corporationId))
            {
                continue;
            }

            var corporationAssets = await FetchCorporationAssetsAsync(account, corporationId, cancellationToken).ConfigureAwait(false);
            assets.AddRange(corporationAssets.Select(asset => new CachedEveAsset
            {
                CharacterId = account.CharacterId,
                CharacterName = account.CharacterName,
                CorporationId = corporationId,
                IsCorporationAsset = true,
                ItemId = asset.ItemId,
                TypeId = asset.TypeId,
                LocationId = asset.LocationId,
                LocationFlag = asset.LocationFlag,
                LocationType = asset.LocationType,
                Quantity = asset.Quantity,
                IsSingleton = asset.IsSingleton,
                IsBlueprintCopy = asset.IsBlueprintCopy,
                CachedAt = DateTimeOffset.UtcNow
            }));
        }

        await PopulateNamesAsync(assets, accounts, cancellationToken).ConfigureAwait(false);
        SaveCachedAssets(cacheFilePath, assets);
        return assets;
    }

    private async Task<IReadOnlyList<EsiAssetDto>> FetchCharacterAssetsAsync(SavedCharacterAccount account, CancellationToken cancellationToken)
    {
        var assets = new List<EsiAssetDto>();
        var page = 1;
        var pages = 1;
        var accessToken = await GetEveAccessTokenAsync(account, forceRefresh: false, cancellationToken).ConfigureAwait(false);

        do
        {
            using var request = CreateAssetRequest(account.CharacterId, accessToken.AccessToken, page);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                accessToken = await GetEveAccessTokenAsync(account, forceRefresh: true, cancellationToken).ConfigureAwait(false);
                using var retryRequest = CreateAssetRequest(account.CharacterId, accessToken.AccessToken, page);
                using var retryResponse = await httpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
                retryResponse.EnsureSuccessStatusCode();
                pages = GetPageCount(retryResponse);
                assets.AddRange(await ReadAssetsAsync(retryResponse, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                response.EnsureSuccessStatusCode();
                pages = GetPageCount(response);
                assets.AddRange(await ReadAssetsAsync(response, cancellationToken).ConfigureAwait(false));
            }

            page++;
        }
        while (page <= pages);

        return assets;
    }

    private async Task<IReadOnlyList<EsiAssetDto>> FetchCorporationAssetsAsync(SavedCharacterAccount account, long corporationId, CancellationToken cancellationToken)
    {
        var assets = new List<EsiAssetDto>();
        var page = 1;
        var pages = 1;
        var accessToken = await GetEveAccessTokenAsync(account, forceRefresh: false, cancellationToken).ConfigureAwait(false);

        do
        {
            using var request = CreateCorporationAssetRequest(corporationId, accessToken.AccessToken, page);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            {
                return assets;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                accessToken = await GetEveAccessTokenAsync(account, forceRefresh: true, cancellationToken).ConfigureAwait(false);
                using var retryRequest = CreateCorporationAssetRequest(corporationId, accessToken.AccessToken, page);
                using var retryResponse = await httpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
                if (retryResponse.StatusCode == HttpStatusCode.Forbidden || retryResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return assets;
                }

                retryResponse.EnsureSuccessStatusCode();
                pages = GetPageCount(retryResponse);
                assets.AddRange(await ReadAssetsAsync(retryResponse, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                response.EnsureSuccessStatusCode();
                pages = GetPageCount(response);
                assets.AddRange(await ReadAssetsAsync(response, cancellationToken).ConfigureAwait(false));
            }

            page++;
        }
        while (page <= pages);

        return assets;
    }

    private static HttpRequestMessage CreateAssetRequest(long characterId, string accessToken, int page)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://esi.evetech.net/latest/characters/{characterId}/assets/?datasource=tranquility&page={page}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static HttpRequestMessage CreateCorporationAssetRequest(long corporationId, string accessToken, int page)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://esi.evetech.net/latest/corporations/{corporationId}/assets/?datasource=tranquility&page={page}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static int GetPageCount(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("X-Pages", out var values)
            && int.TryParse(values.FirstOrDefault(), out var pages)
            ? Math.Max(1, pages)
            : 1;
    }

    private static async Task<IReadOnlyList<EsiAssetDto>> ReadAssetsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<EsiAssetDto>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
    }

    private async Task PopulateNamesAsync(
        List<CachedEveAsset> assets,
        IReadOnlyList<SavedCharacterAccount> accounts,
        CancellationToken cancellationToken)
    {
        var typeIds = assets.Select(asset => asset.TypeId).Distinct().ToList();
        var assetItemIds = assets.Select(asset => asset.ItemId).ToHashSet();
        var publicLocationIds = assets
            .Where(asset => asset.LocationId > 0
                && asset.LocationId < 1_000_000_000_000
                && !assetItemIds.Contains(asset.LocationId))
            .Select(asset => asset.LocationId)
            .Distinct()
            .ToList();

        var assetsByItemId = assets.ToDictionary(asset => asset.ItemId);
        var rootLocationIds = assets
            .Select(asset => FindRootLocationId(asset, assetsByItemId))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var names = await FetchUniverseNamesAsync(typeIds.Concat(publicLocationIds).Concat(rootLocationIds.Where(id => id < 1_000_000_000_000)).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var assetLocationNames = await FetchAssetLocationNamesAsync(assets, accounts, cancellationToken).ConfigureAwait(false);
        var structureInfos = await FetchStructureInfosAsync(assets, rootLocationIds, accounts, cancellationToken).ConfigureAwait(false);
        var stationInfos = await FetchStationInfosAsync(assets, rootLocationIds, cancellationToken).ConfigureAwait(false);
        var solarSystemNames = await FetchUniverseNamesAsync(
            structureInfos.Values.Select(info => info.SolarSystemId)
                .Concat(stationInfos.Values.Select(info => info.SolarSystemId))
                .Where(id => id > 0)
                .Distinct()
                .ToList(),
            cancellationToken).ConfigureAwait(false);

        foreach (var asset in assets)
        {
            asset.TypeName = names.TryGetValue(asset.TypeId, out var typeName) ? typeName : $"Type {asset.TypeId}";
            asset.ItemName = assetLocationNames.TryGetValue((asset.CharacterId, asset.ItemId), out var itemName)
                ? itemName
                : asset.TypeName;
            asset.LocationName =
                assetLocationNames.TryGetValue((asset.CharacterId, asset.LocationId), out var assetLocationName)
                    ? assetLocationName
                    : structureInfos.TryGetValue(asset.LocationId, out var structureInfo)
                        ? structureInfo.Name
                        : stationInfos.TryGetValue(asset.LocationId, out var stationInfo)
                            ? stationInfo.Name
                        : names.TryGetValue(asset.LocationId, out var locationName)
                            ? locationName
                            : FormatUnknownLocation(asset.LocationId, asset.LocationType);
        }

        foreach (var asset in assets)
        {
            var rootLocationId = FindRootLocationId(asset, assetsByItemId);
            asset.RootLocationId = rootLocationId;
            asset.RootLocationName =
                structureInfos.TryGetValue(rootLocationId, out var structureInfo)
                    ? structureInfo.Name
                    : stationInfos.TryGetValue(rootLocationId, out var stationInfo)
                        ? stationInfo.Name
                        : names.TryGetValue(rootLocationId, out var rootName)
                            ? rootName
                            : FormatUnknownLocation(rootLocationId, asset.LocationType);

            var solarSystemId =
                structureInfos.TryGetValue(rootLocationId, out structureInfo)
                    ? structureInfo.SolarSystemId
                    : stationInfos.TryGetValue(rootLocationId, out stationInfo)
                        ? stationInfo.SolarSystemId
                        : 0;

            asset.SolarSystemId = solarSystemId;
            asset.SolarSystemName = solarSystemId > 0 && solarSystemNames.TryGetValue(solarSystemId, out var solarSystemName)
                ? solarSystemName
                : "Unknown System";
        }
    }

    private static long FindRootLocationId(CachedEveAsset asset, IReadOnlyDictionary<long, CachedEveAsset> assetsByItemId)
    {
        var locationId = asset.LocationId;
        var guard = 0;
        while (assetsByItemId.TryGetValue(locationId, out var parentAsset) && guard < 64)
        {
            locationId = parentAsset.LocationId;
            guard++;
        }

        return locationId;
    }

    private async Task<Dictionary<(long CharacterId, long ItemId), string>> FetchAssetLocationNamesAsync(
        IReadOnlyList<CachedEveAsset> assets,
        IReadOnlyList<SavedCharacterAccount> accounts,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<(long CharacterId, long ItemId), string>();
        var accountsByCharacterId = accounts.ToDictionary(account => account.CharacterId, account => account);

        foreach (var characterGroup in assets.GroupBy(asset => asset.CharacterId))
        {
            if (!accountsByCharacterId.TryGetValue(characterGroup.Key, out var account))
            {
                continue;
            }

            var assetItemIds = characterGroup.Select(asset => asset.ItemId).ToHashSet();
            var locationItemIds = characterGroup
                .Where(asset => !asset.IsCorporationAsset && assetItemIds.Contains(asset.LocationId))
                .Select(asset => asset.LocationId)
                .Distinct()
                .ToList();

            foreach (var chunk in locationItemIds.Chunk(1000))
            {
                using var response = await SendAssetNamesRequestAsync(account, chunk, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var rows = await JsonSerializer.DeserializeAsync<List<EsiAssetNameDto>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
                foreach (var row in rows)
                {
                    names[(account.CharacterId, row.ItemId)] = row.Name;
                }
            }
        }

        foreach (var corporationGroup in assets.Where(asset => asset.IsCorporationAsset && asset.CorporationId > 0).GroupBy(asset => asset.CorporationId))
        {
            var account = accounts.FirstOrDefault(account => assets.Any(asset => asset.IsCorporationAsset
                && asset.CorporationId == corporationGroup.Key
                && asset.CharacterId == account.CharacterId));
            if (account is null)
            {
                continue;
            }

            var assetItemIds = corporationGroup.Select(asset => asset.ItemId).ToHashSet();
            var locationItemIds = corporationGroup
                .Where(asset => assetItemIds.Contains(asset.LocationId))
                .Select(asset => asset.LocationId)
                .Distinct()
                .ToList();

            foreach (var chunk in locationItemIds.Chunk(1000))
            {
                using var response = await SendCorporationAssetNamesRequestAsync(account, corporationGroup.Key, chunk, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var rows = await JsonSerializer.DeserializeAsync<List<EsiAssetNameDto>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
                foreach (var row in rows)
                {
                    names[(account.CharacterId, row.ItemId)] = row.Name;
                }
            }
        }

        return names;
    }

    private async Task<HttpResponseMessage> SendAssetNamesRequestAsync(
        SavedCharacterAccount account,
        IEnumerable<long> itemIds,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetEveAccessTokenAsync(account, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(itemIds);
        var response = await SendAssetNamesRequestAsync(account.CharacterId, accessToken.AccessToken, payload, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        accessToken = await GetEveAccessTokenAsync(account, forceRefresh: true, cancellationToken).ConfigureAwait(false);
        return await SendAssetNamesRequestAsync(account.CharacterId, accessToken.AccessToken, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAssetNamesRequestAsync(
        long characterId,
        string accessToken,
        string payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://esi.evetech.net/latest/characters/{characterId}/assets/names/?datasource=tranquility");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCorporationAssetNamesRequestAsync(
        SavedCharacterAccount account,
        long corporationId,
        IEnumerable<long> itemIds,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetEveAccessTokenAsync(account, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(itemIds);
        var response = await SendCorporationAssetNamesRequestAsync(corporationId, accessToken.AccessToken, payload, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        accessToken = await GetEveAccessTokenAsync(account, forceRefresh: true, cancellationToken).ConfigureAwait(false);
        return await SendCorporationAssetNamesRequestAsync(corporationId, accessToken.AccessToken, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCorporationAssetNamesRequestAsync(
        long corporationId,
        string accessToken,
        string payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://esi.evetech.net/latest/corporations/{corporationId}/assets/names/?datasource=tranquility");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<long, StructureInfo>> FetchStructureInfosAsync(
        IReadOnlyList<CachedEveAsset> assets,
        IReadOnlyList<long> rootLocationIds,
        IReadOnlyList<SavedCharacterAccount> accounts,
        CancellationToken cancellationToken)
    {
        var infos = new Dictionary<long, StructureInfo>();
        var assetItemIds = assets.Select(asset => asset.ItemId).ToHashSet();
        var structureIds = assets
            .Where(asset => asset.LocationId >= 1_000_000_000_000 || (asset.LocationId > 70000000 && !assetItemIds.Contains(asset.LocationId)))
            .Select(asset => asset.LocationId)
            .Concat(rootLocationIds.Where(id => id >= 1_000_000_000_000 || id > 70000000))
            .Distinct()
            .ToList();

        foreach (var structureId in structureIds)
        {
            foreach (var account in accounts.Where(account => account.Scopes.Contains("esi-universe.read_structures.v1", StringComparer.Ordinal)))
            {
                using var response = await SendStructureRequestAsync(account, structureId, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var structure = await JsonSerializer.DeserializeAsync<EsiStructureDto>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (structure is not null && !string.IsNullOrWhiteSpace(structure.Name))
                {
                    infos[structureId] = new StructureInfo(structure.Name, structure.SolarSystemId);
                    break;
                }
            }
        }

        return infos;
    }

    private async Task<Dictionary<long, StationInfo>> FetchStationInfosAsync(
        IReadOnlyList<CachedEveAsset> assets,
        IReadOnlyList<long> rootLocationIds,
        CancellationToken cancellationToken)
    {
        var infos = new Dictionary<long, StationInfo>();
        var stationIds = assets
            .Where(asset => asset.LocationId >= 60000000 && asset.LocationId < 70000000)
            .Select(asset => asset.LocationId)
            .Concat(rootLocationIds.Where(id => id >= 60000000 && id < 70000000))
            .Distinct()
            .ToList();

        foreach (var stationId in stationIds)
        {
            using var response = await httpClient.GetAsync(
                $"https://esi.evetech.net/latest/universe/stations/{stationId}/?datasource=tranquility",
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var station = await JsonSerializer.DeserializeAsync<EsiStationDto>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (station is not null && !string.IsNullOrWhiteSpace(station.Name))
            {
                infos[stationId] = new StationInfo(station.Name, station.SolarSystemId);
            }
        }

        return infos;
    }

    private async Task<HttpResponseMessage> SendStructureRequestAsync(
        SavedCharacterAccount account,
        long structureId,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetEveAccessTokenAsync(account, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var response = await SendStructureRequestAsync(structureId, accessToken.AccessToken, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        accessToken = await GetEveAccessTokenAsync(account, forceRefresh: true, cancellationToken).ConfigureAwait(false);
        return await SendStructureRequestAsync(structureId, accessToken.AccessToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendStructureRequestAsync(
        long structureId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://esi.evetech.net/latest/universe/structures/{structureId}/?datasource=tranquility");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EveAccessToken> GetEveAccessTokenAsync(
        SavedCharacterAccount account,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh
            && accessTokenCache.TryGetValue(account.CharacterId, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cached;
        }

        using var request = CreatePocketBaseRequest(
            account,
            HttpMethod.Get,
            $"/api/eve-industry/auth/eve/access-token/{account.CharacterId}");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var token = await JsonSerializer.DeserializeAsync<EveAccessTokenResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("PocketBase did not return a valid EVE access token.");
        }

        var accessToken = new EveAccessToken(
            token.AccessToken,
            token.ExpiresAt);
        accessTokenCache[account.CharacterId] = accessToken;
        return accessToken;
    }

    private HttpRequestMessage CreatePocketBaseRequest(
        SavedCharacterAccount account,
        HttpMethod method,
        string path)
    {
        var baseUrl = pocketBaseUrlProvider().Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("PocketBase server URL is not configured.");
        }

        var builder = new UriBuilder(new Uri(new Uri(EnsureTrailingSlash(baseUrl)), path.TrimStart('/')));
        var request = new HttpRequestMessage(method, builder.Uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.PocketBaseAuthToken);
        return request;
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private async Task<long> FetchCharacterCorporationIdAsync(long characterId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"https://esi.evetech.net/latest/characters/{characterId}/?datasource=tranquility",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var character = await JsonSerializer.DeserializeAsync<EsiCharacterDto>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return character?.CorporationId ?? 0;
    }

    private static string FormatUnknownLocation(long locationId, string locationType)
    {
        return string.IsNullOrWhiteSpace(locationType)
            ? $"Unknown Location {locationId}"
            : $"{locationType} {locationId}";
    }

    private async Task<Dictionary<long, string>> FetchUniverseNamesAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        var names = new Dictionary<long, string>();
        foreach (var chunk in ids.Chunk(1000))
        {
            var json = JsonSerializer.Serialize(chunk);
            using var response = await httpClient.PostAsync(
                "https://esi.evetech.net/latest/universe/names/?datasource=tranquility",
                new StringContent(json, Encoding.UTF8, "application/json"),
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var rows = await JsonSerializer.DeserializeAsync<List<EsiUniverseNameDto>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
            foreach (var row in rows)
            {
                names[row.Id] = row.Name;
            }
        }

        return names;
    }

    private static List<CachedEveAsset> LoadCachedAssets(string cacheFilePath)
    {
        try
        {
            if (!File.Exists(cacheFilePath))
            {
                return [];
            }

            using var stream = File.OpenRead(cacheFilePath);
            return JsonSerializer.Deserialize<List<CachedEveAsset>>(stream, JsonOptions) ?? [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void SaveCachedAssets(string cacheFilePath, IReadOnlyList<CachedEveAsset> assets)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);
            using var stream = File.Create(cacheFilePath);
            JsonSerializer.Serialize(stream, assets, JsonOptions);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetAssetCacheFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "EveIndustryPlanner", "character-assets.json");
    }

    private sealed class EsiAssetDto
    {
        [JsonPropertyName("item_id")]
        public long ItemId { get; init; }

        [JsonPropertyName("type_id")]
        public long TypeId { get; init; }

        [JsonPropertyName("location_id")]
        public long LocationId { get; init; }

        [JsonPropertyName("location_flag")]
        public string LocationFlag { get; init; } = string.Empty;

        [JsonPropertyName("location_type")]
        public string LocationType { get; init; } = string.Empty;

        [JsonPropertyName("quantity")]
        public long Quantity { get; init; } = 1;

        [JsonPropertyName("is_singleton")]
        public bool IsSingleton { get; init; }

        [JsonPropertyName("is_blueprint_copy")]
        public bool IsBlueprintCopy { get; init; }
    }

    private sealed class EsiUniverseNameDto
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class EsiAssetNameDto
    {
        [JsonPropertyName("item_id")]
        public long ItemId { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class EsiStructureDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("solar_system_id")]
        public long SolarSystemId { get; init; }
    }

    private sealed class EsiStationDto
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("system_id")]
        public long SolarSystemId { get; init; }
    }

    private sealed class EsiCharacterDto
    {
        [JsonPropertyName("corporation_id")]
        public long CorporationId { get; init; }
    }

    private sealed record StructureInfo(string Name, long SolarSystemId);

    private sealed record StationInfo(string Name, long SolarSystemId);

    private sealed record EveAccessToken(string AccessToken, DateTimeOffset ExpiresAt);

    private sealed class EveAccessTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; init; }
    }
}

public sealed class CachedEveAsset
{
    public long CharacterId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public long CorporationId { get; init; }
    public bool IsCorporationAsset { get; init; }
    public long ItemId { get; init; }
    public string ItemName { get; set; } = string.Empty;
    public long TypeId { get; init; }
    public string TypeName { get; set; } = string.Empty;
    public long LocationId { get; init; }
    public string LocationName { get; set; } = string.Empty;
    public long RootLocationId { get; set; }
    public string RootLocationName { get; set; } = string.Empty;
    public long SolarSystemId { get; set; }
    public string SolarSystemName { get; set; } = string.Empty;
    public string LocationFlag { get; init; } = string.Empty;
    public string LocationType { get; init; } = string.Empty;
    public long Quantity { get; init; }
    public bool IsSingleton { get; init; }
    public bool IsBlueprintCopy { get; init; }
    public DateTimeOffset CachedAt { get; init; }
}
