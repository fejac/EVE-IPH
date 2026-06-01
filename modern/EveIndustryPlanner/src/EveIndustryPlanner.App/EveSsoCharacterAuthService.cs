using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EveIndustryPlanner.App;

public sealed class EveSsoCharacterAuthService
{
    private const string AuthorizeUrl = "https://login.eveonline.com/v2/oauth/authorize";
    private const string TokenUrl = "https://login.eveonline.com/v2/oauth/token";
    private const string ClientId = "fa12ff70c3954dbd8c19d1c19cb0942c";
    private const string ClientSecret = "J7O6k7SrVTDMIsiSYWN46cZbGmEdu5Uv13Unj8AS";
    private const string RedirectUrl = "http://localhost:8080/callback/";
    private const string CallbackListenPrefix = "http://localhost:8080/callback/";

    private readonly HttpClient httpClient = new();

    public async Task<EveSsoCharacterToken> AddCharacterAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken = default)
    {
        var state = Guid.NewGuid().ToString("N");
        var scopeText = string.Join(' ', scopes.Distinct(StringComparer.Ordinal).Where(scope => !string.IsNullOrWhiteSpace(scope)));

        var loginUrl = AuthorizeUrl
            + "?response_type=code"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUrl)}"
            + $"&client_id={Uri.EscapeDataString(ClientId)}"
            + $"&scope={Uri.EscapeDataString(scopeText)}"
            + $"&state={Uri.EscapeDataString(state)}";

        using var listener = new HttpListener();
        listener.Prefixes.Add(CallbackListenPrefix);
        listener.Start();

        Process.Start(new ProcessStartInfo
        {
            FileName = loginUrl,
            UseShellExecute = true
        });

        var context = await WaitForCallbackAsync(listener, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        var query = ParseQuery(context.Request.Url?.Query ?? string.Empty);

        await WriteBrowserResponseAsync(context.Response, "Login successful. You can close this window.", cancellationToken).ConfigureAwait(false);

        if (!query.TryGetValue("state", out var returnedState) || returnedState != state)
        {
            throw new InvalidOperationException("EVE SSO returned an invalid state value.");
        }

        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("EVE SSO did not return an authorization code.");
        }

        return await ExchangeAuthorizationCodeAsync(code, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EveSsoCharacterToken> RefreshAccessTokenAsync(SavedCharacterAccount account, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            throw new InvalidOperationException("Selected character does not have a refresh token.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = account.RefreshToken
        });

        httpClient.DefaultRequestHeaders.Authorization = CreateBasicAuthorizationHeader();
        using var response = await httpClient.PostAsync(TokenUrl, content, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var token = ParseTokenResponse(responseText);
        var identity = ReadCharacterFromAccessToken(token.AccessToken);

        return new EveSsoCharacterToken(
            identity.CharacterId,
            identity.CharacterName,
            token.AccessToken,
            string.IsNullOrWhiteSpace(token.RefreshToken) ? account.RefreshToken : token.RefreshToken,
            token.TokenType,
            token.ExpiresIn,
            identity.Scopes);
    }

    private async Task<EveSsoCharacterToken> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code
        });

        httpClient.DefaultRequestHeaders.Authorization = CreateBasicAuthorizationHeader();
        using var response = await httpClient.PostAsync(TokenUrl, content, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var token = ParseTokenResponse(responseText);
        var identity = ReadCharacterFromAccessToken(token.AccessToken);

        return new EveSsoCharacterToken(
            identity.CharacterId,
            identity.CharacterName,
            token.AccessToken,
            token.RefreshToken,
            token.TokenType,
            token.ExpiresIn,
            identity.Scopes);
    }

    private static async Task<HttpListenerContext> WaitForCallbackAsync(HttpListener listener, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var callbackTask = listener.GetContextAsync();
        var completedTask = await Task.WhenAny(callbackTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);

        if (completedTask != callbackTask)
        {
            throw new TimeoutException("Timed out waiting for the EVE SSO callback.");
        }

        return await callbackTask.ConfigureAwait(false);
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, string message, CancellationToken cancellationToken)
    {
        var buffer = Encoding.UTF8.GetBytes($"<html><body><h2>{WebUtility.HtmlEncode(message)}</h2></body></html>");
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    private static TokenResponse ParseTokenResponse(string responseText)
    {
        using var json = JsonDocument.Parse(responseText);
        var root = json.RootElement;

        return new TokenResponse(
            root.GetProperty("access_token").GetString() ?? string.Empty,
            root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("token_type", out var tokenType) ? tokenType.GetString() ?? "Bearer" : "Bearer",
            root.TryGetProperty("expires_in", out var expiresIn) ? expiresIn.GetInt32() : 1200);
    }

    private static CharacterIdentity ReadCharacterFromAccessToken(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("EVE SSO returned an invalid access token.");
        }

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var payload = JsonDocument.Parse(payloadJson);
        var root = payload.RootElement;

        var characterName = root.TryGetProperty("name", out var nameClaim)
            ? nameClaim.GetString() ?? string.Empty
            : string.Empty;

        if (!root.TryGetProperty("sub", out var subClaim))
        {
            throw new InvalidOperationException("EVE SSO access token does not contain a subject claim.");
        }

        var characterIdText = (subClaim.GetString() ?? string.Empty).Split(':').LastOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(characterName) || !long.TryParse(characterIdText, out var characterId))
        {
            throw new InvalidOperationException("EVE SSO access token does not contain character identity.");
        }

        var scopes = root.TryGetProperty("scp", out var scopeClaim) && scopeClaim.ValueKind == JsonValueKind.Array
            ? scopeClaim.EnumerateArray().Select(scope => scope.GetString() ?? string.Empty).Where(scope => scope.Length > 0).ToList()
            : [];

        return new CharacterIdentity(characterId, characterName, scopes);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1].Replace("+", " ")),
                StringComparer.Ordinal);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    private static AuthenticationHeaderValue CreateBasicAuthorizationHeader()
    {
        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
        return new AuthenticationHeaderValue("Basic", value);
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

    private sealed record CharacterIdentity(long CharacterId, string CharacterName, IReadOnlyList<string> Scopes);
}

public sealed record EveSsoCharacterToken(
    long CharacterId,
    string CharacterName,
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    IReadOnlyList<string> Scopes);
