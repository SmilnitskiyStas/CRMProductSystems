using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ShelfGuard.Application.Features.Marketplace.Vchasno;

namespace ShelfGuard.Infrastructure.Integrations.Vchasno;

/// <summary>
/// Вчасно (vchasno.ua) EDO public API v2 client (TASK-317).
///
/// Request shapes follow the Вчасно public API collection
/// (https://edo.vchasno.ua/api/v2, Postman «Vchasno.EDO Public API»):
///   • auth — API token in the <c>Authorization</c> header (raw token, generated in
///     Вчасно company settings → employees → «token for API automation»);
///   • upload — <c>POST /documents</c>, multipart/form-data with the PDF in the
///     <c>file</c> field plus optional metadata fields;
///   • status — <c>GET /documents/{id}</c>.
///
/// TODO(TASK-317): the official docs (docs.vchasno.ua) were unreachable from the dev
/// environment when this was written — the exact multipart field names / response
/// envelope may need a one-file adjustment here after the first live call. Everything
/// Вчасно-specific is deliberately isolated in this class.
/// </summary>
public sealed class VchasnoClient : IVchasnoClient
{
    public const string DefaultBaseUrl = "https://edo.vchasno.ua/api/v2";

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public VchasnoClient(HttpClient http, string apiKey, string? baseUrl = null)
    {
        _http = http;
        _apiKey = apiKey;
        _http.BaseAddress = new Uri((string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl)
            .TrimEnd('/') + "/");
    }

    public async Task<string> UploadDocumentAsync(
        string fileName, byte[] pdfBytes, string? recipientEdrpou, string? recipientEmail, string title,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(title), "title");
        if (!string.IsNullOrWhiteSpace(recipientEdrpou))
            form.Add(new StringContent(recipientEdrpou), "edrpou_recipient");
        if (!string.IsNullOrWhiteSpace(recipientEmail))
            form.Add(new StringContent(recipientEmail), "email_recipient");

        using var request = new HttpRequestMessage(HttpMethod.Post, "documents") { Content = form };
        request.Headers.TryAddWithoutValidation("Authorization", _apiKey);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Vchasno upload failed ({(int)response.StatusCode}): {Truncate(body)}");

        var documentId = ExtractDocumentId(body);
        if (documentId is null)
            throw new HttpRequestException(
                $"Vchasno upload succeeded but no document id found in response: {Truncate(body)}");

        return documentId;
    }

    public async Task<string?> GetDocumentStatusAsync(string documentId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"documents/{Uri.EscapeDataString(documentId)}");
        request.Headers.TryAddWithoutValidation("Authorization", _apiKey);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Vchasno status check failed ({(int)response.StatusCode}): {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("status", out var status))
            return status.ValueKind == JsonValueKind.String
                ? status.GetString()
                : status.GetRawText();

        return null;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Handles both envelope variants: { "id": ... } and { "documents": [ { "id": ... } ] }.</summary>
    private static string? ExtractDocumentId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    return id.GetString();

                if (root.TryGetProperty("documents", out var docs)
                    && docs.ValueKind == JsonValueKind.Array
                    && docs.GetArrayLength() > 0
                    && docs[0].TryGetProperty("id", out var firstId)
                    && firstId.ValueKind == JsonValueKind.String)
                    return firstId.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value, int max = 500) =>
        value.Length <= max ? value : value[..max] + "…";
}
