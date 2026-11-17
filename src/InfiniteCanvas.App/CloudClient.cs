using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using InfiniteCanvas.App;

namespace InfiniteCanvas.App;

/// <summary>
/// Client vers le serveur GreenBoard Cloud (ASP.NET Core 9 sur Render).
/// Mode hors-ligne par défaut : aucune requête n'est émise tant que le serveur
/// n'est pas configuré et joignable. GreenBoard reste 100 % local sans compte.
/// </summary>
public sealed class CloudClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly bool _isOffline;

    private CloudClient(HttpMessageHandler handler, bool isOffline, Uri? baseAddress = null)
    {
        _isOffline = isOffline;
        _http = new HttpClient(handler);
        if (baseAddress != null) _http.BaseAddress = baseAddress;
        _http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Crée un client en mode hors-ligne (aucune requête réseau).</summary>
    public static CloudClient Offline() => new(new OfflineHandler(), true);

    /// <summary>Crée un client pointant vers une URL de serveur (déploiement Render).</summary>
    public static CloudClient ForServer(string baseUrl) =>
        new(new HttpClientHandler(), false, new Uri(baseUrl));

    public bool IsOffline => _isOffline;

    // =====================================================================
    // AUTH
    // =====================================================================

    public async Task<AuthResult?> RegisterAsync(string email, string password, string? displayName, CancellationToken ct)
    {
        if (IsOffline) return AuthResult.Offline("Serveur non configuré");
        var payload = new { email, password, displayName };
        return await PostAsync<AuthResult>("/api/v1/auth/register", payload, ct);
    }

    public async Task<AuthResult?> LoginAsync(string email, string password, CancellationToken ct)
    {
        if (IsOffline) return AuthResult.Offline("Serveur non configuré");
        var payload = new { email, password };
        return await PostAsync<AuthResult>("/api/v1/auth/login", payload, ct);
    }

    public async Task<AuthResult?> RefreshAsync(string accessToken, string refreshToken, CancellationToken ct)
    {
        if (IsOffline) return AuthResult.Offline("Serveur non configuré");
        var payload = new { accessToken, refreshToken };
        return await PostAsync<AuthResult>("/api/v1/auth/refresh", payload, ct);
    }

    public async Task<bool> LogoutAsync(string accessToken, CancellationToken ct)
    {
        if (IsOffline) return true;
        return await PostAsync<object?>("/api/v1/auth/logout", null, ct, accessToken) is not null;
    }

    // =====================================================================
    // DOCUMENTS
    // =====================================================================

    public async Task<List<CloudDocument>?> ListDocumentsAsync(string accessToken, CancellationToken ct)
    {
        if (IsOffline) return new List<CloudDocument>();
        return await GetAsync<List<CloudDocument>>("/api/v1/documents", ct, accessToken) ?? new List<CloudDocument>();
    }

    public async Task<CloudDocumentContent?> DownloadDocumentAsync(string accessToken, string documentId, CancellationToken ct)
    {
        if (IsOffline) return null;
        return await GetAsync<CloudDocumentContent>($"/api/v1/documents/{documentId}", ct, accessToken);
    }

    public async Task<CloudDocument?> UploadDocumentAsync(string accessToken, string title, byte[] gboardBytes, int pageCount, CancellationToken ct)
    {
        if (IsOffline) return null;
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(gboardBytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", $"{title}.gboard");
        return await PostAsync<CloudDocument>($"/api/v1/documents?title={Uri.EscapeDataString(title)}", form, ct, accessToken);
    }

    public async Task<bool> DeleteDocumentAsync(string accessToken, string documentId, CancellationToken ct)
    {
        if (IsOffline) return true;
        var resp = await _http.DeleteAsync($"/api/v1/documents/{documentId}", ct);
        return resp.IsSuccessStatusCode;
    }

    // =====================================================================
    // HELPERS INTERNES
    // =====================================================================

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct, string? accessToken = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (accessToken != null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return default;
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, _json);
    }

    private async Task<T?> PostAsync<T>(string path, object? payload, CancellationToken ct, string? accessToken = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (accessToken != null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (payload is MultipartFormDataContent form)
            req.Content = form;
        else if (payload != null)
            req.Content = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return default;
        var json = await resp.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, _json);
    }

    public void Dispose() => _http.Dispose();

    // =====================================================================
    // HANDLER HORS-LIGNE (aucune requête réseau)
    // =====================================================================

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Retourne une réponse vide sans toucher au réseau
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"error\":\"serveur hors-ligne\"}")
            });
        }
    }
}

/// <summary>Résultat d'authentification.</summary>
public sealed record AuthResult
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int ExpiresIn { get; init; }
    public UserInfo? User { get; init; }
    public string? Error { get; init; }
    public bool IsOffline { get; init; }

    public static AuthResult Offline(string error) => new() { IsOffline = true, Error = error };
}

/// <summary>Informations utilisateur retournées par le serveur.</summary>
public sealed record UserInfo(string Id, string Email, string DisplayName, bool EmailConfirmed);

/// <summary>Document dans l'espace cloud de l'utilisateur.</summary>
public sealed record CloudDocument
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public int PageCount { get; init; }
    public DateTimeOffset ModifiedAt { get; init; }
    public string Version { get; init; } = string.Empty;
}

/// <summary>Contenu d'un document téléchargé.</summary>
public sealed record CloudDocumentContent
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public string Version { get; init; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; init; }
}
