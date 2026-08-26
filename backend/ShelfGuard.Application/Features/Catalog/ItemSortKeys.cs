namespace ShelfGuard.Application.Features.Catalog;

/// <summary>
/// Allowlist source of truth for <c>GET api/items</c>'s <c>sortBy</c> query param (TASK-632).
/// Same shape as <c>ReceiptSortKeys</c>/<c>TransferSortKeys</c>/<c>WriteOffSortKeys</c>/
/// <c>StockSortKeys</c> — an unrecognized/omitted value silently normalizes to the default
/// rather than throwing (sorting is a display nicety, never worth a 400). The raw <c>sortBy</c>
/// string is only ever compared against this fixed set here and in <c>ItemRepository</c>'s
/// OrderBy switch — never used to build an expression dynamically.
///
/// "barcode" is included in the allowlist (frontend contract) but <c>ItemRepository</c> maps it
/// to the same order as "name" — <c>Item.Barcodes</c> is a jsonb-mapped <c>List&lt;string&gt;</c>
/// with no natural single sortable scalar, and this codebase has a documented history of jsonb
/// LINQ shapes that build fine but fail against real Postgres (see
/// <c>ItemRepository.GetByBarcodeAsync</c>'s comment). See <c>ItemRepository.ApplySort</c> for
/// the documented judgment call.
/// </summary>
public static class ItemSortKeys
{
    public const string Default = "name";

    private static readonly HashSet<string> Keys =
        ["name", "barcode", "category", "purchaseprice", "retailprice", "minstock", "maxstock"];

    public static string Normalize(string? sortBy)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key is not null && Keys.Contains(key) ? key : Default;
    }
}
