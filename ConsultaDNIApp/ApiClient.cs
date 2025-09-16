using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace Consulta_DNI.REST;

public sealed class ApiClient
{
    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ConsultaDNIApp/1.3 (+linux-gtk)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public Task<string?> GetStringAsync(string url, string? bearerToken = null, CancellationToken ct = default)
        => SendAndReadAsync(HttpMethod.Get, url, null, bearerToken, ct);

    public Task<string?> PostJsonGetStringAsync(string url, object? payload, string? bearerToken = null, CancellationToken ct = default)
    {
        string? raw = payload is null ? null : JsonConvert.SerializeObject(payload);
        return PostRawJsonGetStringAsync(url, raw, bearerToken, ct);
    }

    public Task<string?> PostRawJsonGetStringAsync(string url, string? rawJson, string? bearerToken = null, CancellationToken ct = default)
        => SendAndReadAsync(HttpMethod.Post, url, rawJson, bearerToken, ct);

    private async Task<string?> SendAndReadAsync(HttpMethod method, string url, string? rawJson, string? bearerToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL vacía", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new ArgumentException("URL inválida", nameof(url));

        using var request = new HttpRequestMessage(method, uri);

        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());

        if (rawJson is not null)
            request.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        var raw = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return WebUtility.HtmlDecode(raw);
    }
}
