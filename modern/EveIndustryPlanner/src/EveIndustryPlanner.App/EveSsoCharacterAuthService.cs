using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EveIndustryPlanner.App;

public sealed class EveSsoCharacterAuthService
{
    private const string AuthorizeUrl = "https://login.eveonline.com/v2/oauth/authorize";
    private const string ClientId = "fa12ff70c3954dbd8c19d1c19cb0942c";
    private const string RedirectUrl = "http://localhost:8080/callback/";
    private const string CallbackListenPrefix = "http://localhost:8080/callback/";
    private const string PocketBaseEveCallbackPath = "/api/eve-industry/auth/eve/callback";

    private readonly HttpClient httpClient = new();

    public async Task<EveSsoCharacterToken> AddCharacterViaPocketBaseAsync(string pocketBaseUrl, IReadOnlyList<string> scopes, string? pocketBaseAuthToken = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pocketBaseUrl))
        {
            throw new ArgumentException("PocketBase URL is required.", nameof(pocketBaseUrl));
        }

        var callback = await BeginEveSsoAsync(scopes, cancellationToken).ConfigureAwait(false);
        return await ExchangeAuthorizationCodeWithPocketBaseAsync(pocketBaseUrl, callback, pocketBaseAuthToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EveSsoCallback> BeginEveSsoAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        var codeVerifier = CreateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var scopeText = string.Join(' ', scopes.Distinct(StringComparer.Ordinal).Where(scope => !string.IsNullOrWhiteSpace(scope)));

        var loginUrl = AuthorizeUrl
            + "?response_type=code"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUrl)}"
            + $"&client_id={Uri.EscapeDataString(ClientId)}"
            + $"&scope={Uri.EscapeDataString(scopeText)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256";

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

        return new EveSsoCallback(code, codeVerifier, RedirectUrl);
    }

    private async Task<EveSsoCharacterToken> ExchangeAuthorizationCodeWithPocketBaseAsync(
        string pocketBaseUrl,
        EveSsoCallback callback,
        string? pocketBaseAuthToken,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(new Uri(EnsureTrailingSlash(pocketBaseUrl)), PocketBaseEveCallbackPath.TrimStart('/'));
        var payload = JsonSerializer.Serialize(new
        {
            code = callback.Code,
            code_verifier = callback.CodeVerifier,
            redirect_uri = callback.RedirectUri
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(pocketBaseAuthToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pocketBaseAuthToken);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var response = await httpClient.SendAsync(request, linkedCancellation.Token).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PocketBase EVE SSO exchange failed: {ExtractPocketBaseError(responseText)}");
        }

        using var json = JsonDocument.Parse(responseText);
        var root = json.RootElement;
        var authToken = root.GetProperty("token").GetString() ?? string.Empty;
        var eveAccount = root.GetProperty("eve_account");
        var characterIdText = eveAccount.GetProperty("character_id").GetString() ?? string.Empty;
        var characterName = eveAccount.GetProperty("character_name").GetString() ?? string.Empty;
        var scopes = eveAccount.TryGetProperty("scopes", out var scopesElement)
            ? ReadScopes(scopesElement)
            : [];

        if (!long.TryParse(characterIdText, out var characterId) || characterName.Length == 0 || authToken.Length == 0)
        {
            throw new InvalidOperationException("PocketBase EVE SSO response did not contain a valid character identity.");
        }

        return new EveSsoCharacterToken(
            characterId,
            characterName,
            authToken,
            scopes);
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

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private static IReadOnlyList<string> ReadScopes(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray()
                .Select(scope => scope.GetString() ?? string.Empty)
                .Where(scope => scope.Length > 0)
                .ToList(),
            JsonValueKind.String => (element.GetString() ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            _ => []
        };
    }

    private static string ExtractPocketBaseError(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return "Empty server response.";
        }

        try
        {
            using var json = JsonDocument.Parse(responseText);
            var root = json.RootElement;
            if (root.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? responseText;
            }
        }
        catch (JsonException)
        {
        }

        return responseText;
    }

    private sealed record EveSsoCallback(string Code, string CodeVerifier, string RedirectUri);
}

public sealed record EveSsoCharacterToken(
    long CharacterId,
    string CharacterName,
    string PocketBaseAuthToken,
    IReadOnlyList<string> Scopes);
