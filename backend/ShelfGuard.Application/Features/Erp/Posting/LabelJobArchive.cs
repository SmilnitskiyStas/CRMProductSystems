using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record LabelJobUpload(Guid OperationId, string Payload);
public sealed record LabelJobArchiveAck(Guid OperationId, Guid JobId, string CommandHash, string CheckpointHash, int AcceptedEvents, int ServerEvents, string ServerStatus, DateTime ReceivedAt, bool Replayed);
public sealed record LabelJobArchiveView(Guid Id, Guid LocationId, Guid CreatedBy, string Origin, bool DevelopmentOnly, int EventCount, string Status, DateTime ReceivedAt, string Payload);
public interface ILabelJobArchive
{
    Task<LabelJobArchiveAck> UploadAsync(LabelJobUpload command, CancellationToken ct);
    Task<ProductionDesktopPage<LabelJobArchiveView>> ListAsync(Guid locationId, int page, bool allActors, CancellationToken ct, int maxJobProtocol = 1);
    Task<LabelJobArchiveView> GetAsync(Guid locationId, Guid id, CancellationToken ct, int maxJobProtocol = 1);
}
public sealed record LabelJobArchiveEvent(int Seq, string Status, int Attempt, string Detail, DateTime OccurredAt, string ContentHash, string Payload);
public sealed record ParsedLabelJob(Guid Id, Guid TenantId, Guid ActorId, Guid LocationId, string CommandHash, string ArtifactHash, string ContextHash, string IntentHash,
    int Copies, bool RequiresBulk, Guid BatchId, string Scenario, DateTime CreatedAt, string TemplateKind, Guid TemplateId, Guid TemplateKey, int TemplateVersion, string TemplateHash, LabelLayout Layout,
    JsonElement? Price, IReadOnlyList<LabelJobArchiveEvent> Events, JsonElement? Receipt, string Payload, string CheckpointHash, string JobProtocol = "label-job.v1", JsonElement? Printer = null);

public static class LabelJobArchiveRules
{
    public const string Protocol = "labels.jobs.archive.v1";
    public static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    private static ArgumentException Invalid() => new("Invalid label job checkpoint.");
    private static void Fields(JsonElement e, params string[] fields)
    {
        if (e.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = e.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct().Count() != names.Length || names.Except(fields).Any()) throw Invalid();
    }
    private static JsonElement P(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v : throw Invalid();
    private static string S(JsonElement e, string name, int max = 200)
    {
        var v = P(e, name); if (v.ValueKind != JsonValueKind.String || v.GetString() is not { } s || s.Length is < 1 || s.Length > max || s.Any(char.IsControl)) throw Invalid(); return s;
    }
    private static Guid G(JsonElement e, string name) => Guid.TryParseExact(S(e, name, 36), "D", out var id) && id != Guid.Empty ? id : throw Invalid();
    private static int N(JsonElement e, string name, int min, int max) => P(e, name).TryGetInt32(out var n) && n >= min && n <= max ? n : throw Invalid();
    private static bool B(JsonElement e, string name) => P(e, name).ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw Invalid() };
    private static DateTime At(JsonElement e, string name) => P(e, name).ValueKind == JsonValueKind.String && P(e, name).TryGetDateTimeOffset(out var v) && v.Offset == TimeSpan.Zero && v.Year is >= 2020 and <= 2100 ? v.UtcDateTime : throw Invalid();
    private static string Fingerprint(JsonElement e, string name) => PricingRules.Fingerprint(S(e, name, 64));
    // Known protocol objects have no integer property names; retain their JS insertion order.
    // All numeric fields are bounded integers or exact hundredths, so decimal formatting matches JSON.stringify.
    public static string Canonical(JsonElement e) => e.ValueKind switch {
        JsonValueKind.Object => "{" + string.Join(",", e.EnumerateObject().Select(p => Quote(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", e.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.String => Quote(e.GetString()!),
        JsonValueKind.Number => decimal.TryParse(e.GetRawText(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture) : throw Invalid(),
        JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Null => "null", _ => throw Invalid()
    };
    private static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s) b.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\t' => "\\t", '\n' => "\\n", '\f' => "\\f", '\r' => "\\r", < ' ' => "\\u" + ((int)c).ToString("x4"), _ => c.ToString() });
        return b.Append('"').ToString();
    }
    public static bool Transition(string prior, int before, string status, int attempt) => status switch {
        "running" => prior is "queued" or "failed" or "unknown" && attempt == before + 1 && attempt <= 3,
        "cancelled" => prior is "queued" or "failed" && attempt == before,
        "unknown" => prior == "running" && attempt == before,
        "failed" or "succeeded" => prior is "running" or "unknown" && attempt == before,
        _ => false
    };
    public static bool PriceMatches(JsonElement client, string archivedPayload, string archivedHash)
    {
        try
        {
            Fields(client, "offer", "contentHash", "replayed"); var offer = P(client, "offer");
            Fields(offer, "snapshotId", "tenantId", "locationId", "itemId", "itemName", "unit", "currency", "at", "createdAt", "plannedPrice", "legacyBasePrice", "plannedVatRate", "legacyVatRate", "priceListId", "listKey", "version", "source", "policy", "nextBoundary", "differs", "warnings");
            foreach (var key in new[] { "snapshotId", "tenantId", "locationId", "itemId", "priceListId", "listKey" }) G(offer, key);
            var name = S(offer, "itemName", 255); var unit = S(offer, "unit", 20); if (name != name.Trim() || unit != unit.Trim()) return false;
            S(offer, "createdAt", 40); N(offer, "version", 1, 100000000); B(offer, "differs");
            foreach (var key in new[] { "at", "nextBoundary" })
            {
                if (key == "nextBoundary" && P(offer, key).ValueKind == JsonValueKind.Null) continue;
                if (!System.Text.RegularExpressions.Regex.IsMatch(S(offer, key, 30), @"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.000)?Z$")) return false; At(offer, key);
            }
            foreach (var field in new[] { "plannedPrice", "legacyBasePrice", "plannedVatRate", "legacyVatRate" })
            {
                var value = P(offer, field); if (field == "legacyBasePrice" && value.ValueKind == JsonValueKind.Null) continue;
                if (value.ValueKind != JsonValueKind.String || !System.Text.RegularExpressions.Regex.IsMatch(value.GetString()!, @"^-?(0|[1-9][0-9]*)\.[0-9]{2}$")) return false;
            }
            if (Fingerprint(client, "contentHash") != archivedHash || !B(client, "replayed")) return false;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
            var incoming = offer.Deserialize<PricingOffer>(options)!; var archived = JsonSerializer.Deserialize<PricingOffer>(archivedPayload, options)!;
            if (archived.Source != "planned_price_list" || archived.PlannedPrice == null || archived.PriceListId == null || archived.ListKey == null || archived.Version == null || archived.Policy != "planned-base.v1") return false;
            var empty = Array.Empty<string>();
            return incoming.Warnings != null && incoming.Warnings.SequenceEqual(archived.Warnings) &&
                incoming with { Warnings = empty } == archived with { ItemName = archived.ItemName.Trim(), Unit = archived.Unit.Trim(), Warnings = empty };
        }
        catch (Exception e) when (e is ArgumentException or JsonException or InvalidOperationException or FormatException) { return false; }
    }
    public static ParsedLabelJob Parse(LabelJobUpload upload)
    {
        if (upload.OperationId == Guid.Empty || upload.Payload == null || upload.Payload.Length is < 100 or > 24000 || Encoding.UTF8.GetByteCount(upload.Payload) > 64000) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(upload.Payload, new() { MaxDepth = 16 }); var j = document.RootElement;
            if (Canonical(j) != upload.Payload) throw Invalid();
            var hasReceipt = j.TryGetProperty("receipt", out var receipt);
            var jobProtocol = S(j, "protocol"); if (jobProtocol is not ("label-job.v1" or "label-job.v2")) throw Invalid();
            var allowed = new List<string> { "requestId", "protocol", "status", "createdAt", "render", "copies", "requiresBulk", "scenario", "batchId", "artifactHash", "commandHash", "attempts", "events" }; if (hasReceipt) allowed.Add("receipt"); if (jobProtocol == "label-job.v2") allowed.Add("printer"); Fields(j, allowed.ToArray());
            var id = G(j, "requestId"); var copies = N(j, "copies", 1, 20); var bulk = B(j, "requiresBulk"); if (copies > 1 && !bulk) throw Invalid();
            var scenario = S(j, "scenario"); if (scenario is not ("ok" or "offline-once" or "lost-ack-once" or "offline")) throw Invalid();
            var batch = G(j, "batchId"); var artifact = Fingerprint(j, "artifactHash"); var commandHash = Fingerprint(j, "commandHash"); var created = At(j, "createdAt");
            var render = P(j, "render"); Fields(render, "requestId", "status", "createdAt", "context", "contentHash"); G(render, "requestId"); At(render, "createdAt");
            if (S(render, "status") is not ("prepared" or "exported")) throw Invalid();
            var context = P(render, "context"); Fields(context, "protocol", "server", "tenantId", "actorId", "locationId", "template", "price");
            if (S(context, "protocol") != "label.render.v1" || !Uri.TryCreate(S(context, "server", 2048), UriKind.Absolute, out var server) || server.UserInfo.Length > 0 || server.AbsolutePath != "/" || server.Query.Length > 0 || server.Fragment.Length > 0 || !(server.Scheme == "https" || server.Scheme == "http" && server.IsLoopback)) throw Invalid();
            var tenant = G(context, "tenantId"); var actor = G(context, "actorId"); var location = G(context, "locationId"); var contextHash = Fingerprint(render, "contentHash");
            // Payload is a JSON string carrying the original JS serialization. PostgreSQL json (not jsonb) preserves it.
            if (Hash(context.GetRawText()) != contextHash) throw Invalid();
            var t = P(context, "template"); Fields(t, "kind", "revisionId", "templateKey", "version", "contentHash", "data");
            var kind = S(t, "kind"); if (kind is not ("local" or "server")) throw Invalid(); var template = G(t, "revisionId"); var templateKey = G(t, "templateKey"); var version = N(t, "version", 1, 10000); var templateHash = Fingerprint(t, "contentHash");
            var layout = P(t, "data"); Fields(layout, "name", "widthMm", "heightMm", "dpi", "elements");
            var elements = P(layout, "elements"); if (elements.ValueKind != JsonValueKind.Array) throw Invalid();
            foreach (var e in elements.EnumerateArray()) Fields(e, "id", "field", "text", "x", "y", "width", "height", "fontMm", "bold");
            var data = LabelTemplateRules.Normalize(layout.Deserialize<LabelLayout>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict })!);
            var normalized = Canonical(JsonSerializer.SerializeToElement(data, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            if (normalized != layout.GetRawText()) throw Invalid();
            if (kind == "local" && Hash(layout.GetRawText()) != templateHash) throw Invalid();
            JsonElement? printer = null;
            if (jobProtocol == "label-job.v2") { var binding = P(j, "printer"); LabelPrinterRules.ValidateBinding(binding, location, data); printer = binding.Clone(); }
            var computedCommand = printer is { } bound ? JsonSerializer.Serialize(new { protocol = jobProtocol, id = S(j, "requestId"), renderHash = contextHash, copies, requiresBulk = bulk, scenario, batchId = S(j, "batchId"), artifactHash = artifact, printerHash = bound.GetProperty("bindingHash").GetString() }) : JsonSerializer.Serialize(new { protocol = jobProtocol, id = S(j, "requestId"), renderHash = contextHash, copies, requiresBulk = bulk, scenario, batchId = S(j, "batchId"), artifactHash = artifact });
            if (Hash(computedCommand) != commandHash) throw Invalid();
            var eventArray = P(j, "events"); if (eventArray.ValueKind != JsonValueKind.Array || eventArray.GetArrayLength() is < 1 or > 16) throw Invalid();
            var events = new List<LabelJobArchiveEvent>();
            foreach (var e in eventArray.EnumerateArray())
            {
                Fields(e, "seq", "at", "status", "attempt", "detail"); var seq = N(e, "seq", 1, 16); var state = S(e, "status", 20); var attempt = N(e, "attempt", 0, 3); var at = At(e, "at"); var detail = S(e, "detail", 100);
                if (detail is not ("created" or "restart_result_unknown" or "receipt_reconciled" or "retry_limit_no_receipt" or "simulator_dispatch" or "simulator_accepted" or "simulator_offline" or "adapter_result_unknown" or "user_cancelled")) throw Invalid();
                if (seq != events.Count + 1 || events.Count == 0 && (state != "queued" || attempt != 0 || at != created || detail != "created") || events.Count > 0 && !Transition(events[^1].Status, events[^1].Attempt, state, attempt)) throw Invalid();
                events.Add(new(seq, state, attempt, detail, at, Hash(e.GetRawText()), e.GetRawText()));
            }
            var last = events[^1]; if (S(j, "status") != last.Status || N(j, "attempts", 0, 3) != last.Attempt || hasReceipt != (last.Status == "succeeded")) throw Invalid();
            if (hasReceipt)
            {
                Fields(receipt, "requestId", "status", "adapter", "commandHash", "artifactHash", "copies", "acceptedAt");
                if (G(receipt, "requestId") != id || S(receipt, "status") != "accepted" || S(receipt, "adapter") != "label-simulator.v1" || Fingerprint(receipt, "commandHash") != commandHash || Fingerprint(receipt, "artifactHash") != artifact || N(receipt, "copies", 1, 20) != copies) throw Invalid(); At(receipt, "acceptedAt");
            }
            var price = P(context, "price"); if (price.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) throw Invalid();
            var intent = Hash(JsonSerializer.Serialize(new { createdAt = S(j, "createdAt"), render = render.GetRawText(), commandHash }));
            return new(id, tenant, actor, location, commandHash, artifact, contextHash, intent, copies, bulk, batch, scenario, created, kind, template, templateKey, version, templateHash, data,
                price.ValueKind == JsonValueKind.Null ? null : price.Clone(), events, hasReceipt ? receipt.Clone() : null, upload.Payload, Hash(upload.Payload), jobProtocol, printer);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException) { throw Invalid(); }
    }
}
