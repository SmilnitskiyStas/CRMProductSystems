using System.Globalization;

namespace ShelfGuard.Tools.PchilkaImport.Source;

/// <summary>
/// Read-only access to the Pchilka POS export, via <see cref="PchilkaCliClient"/> (docker exec
/// into the container's Unix socket — see that class's remarks for why not a direct TCP
/// connection). Every method here issues SELECT only — this tool must never write to the
/// source database (TASK-513 brief).
///
/// SQL is built by direct string interpolation here rather than bound parameters, since the
/// CLI transport has no parameterized-query concept. This is safe because every interpolated
/// value is either: (a) a value this tool itself produced (product codes from
/// GetTopProductCodesAsync, cast to long and formatted as digits — never external/user input),
/// or (b) a shop code / DateOnly from this tool's own ImportOptions config file. No value here
/// ever originates from an untrusted source.
/// </summary>
public sealed class PchilkaSourceReader
{
    public async Task<List<(long ProductCode, decimal Qty, decimal AvgUnitPrice)>> GetTopProductCodesAsync(
        int shopCode, DateOnly from, DateOnly to, int topN, CancellationToken ct)
    {
        // AvgUnitPrice = net (post-discount) revenue per unit over the ranking window — a
        // realistic stand-in for PriceRetail (the source has no separate price-list table
        // covered by TASK-513's scope).
        var sql = $"""
            SELECT oi.product_code, SUM(oi.quantity) AS qty,
                   SUM(oi.line_total) / NULLIF(SUM(oi.quantity), 0) AS avg_unit_price
            FROM pos_order_items oi
            JOIN pos_orders o
              ON o.shop_code = oi.shop_code AND o.workplace_id = oi.workplace_id AND o.order_code = oi.order_code
            WHERE oi.shop_code = {shopCode} AND o.order_day BETWEEN '{Sql(from)}' AND '{Sql(to)}' AND oi.quantity > 0
            GROUP BY oi.product_code
            ORDER BY qty DESC
            LIMIT {topN}
            """;

        var rows = await PchilkaCliClient.QueryAsync(sql, ct);
        var result = new List<(long, decimal, decimal)>(rows.Count);
        foreach (var row in rows)
            result.Add((
                long.Parse(row[0], CultureInfo.InvariantCulture),
                ParseDecimal(row[1]) ?? 0m,
                ParseDecimal(row[2]) ?? 0m));
        return result;
    }

    public async Task<List<PchilkaProduct>> GetProductCatalogAsync(
        IReadOnlyList<(long ProductCode, decimal Qty, decimal AvgUnitPrice)> topProducts, CancellationToken ct)
    {
        if (topProducts.Count == 0) return [];

        var codes = topProducts.Select(t => t.ProductCode).ToList();
        var qtyByCode = topProducts.ToDictionary(t => t.ProductCode, t => t.Qty);
        var priceByCode = topProducts.ToDictionary(t => t.ProductCode, t => t.AvgUnitPrice);
        var inClause = string.Join(",", codes);

        var products = new Dictionary<long, PchilkaProduct>();
        {
            var sql = $"""
                SELECT p.product_code, p.product_name, p.group_code, g.group_name, u.unit_abbr, p.vat
                FROM pos_products p
                LEFT JOIN pos_product_groups g ON g.group_code = p.group_code
                LEFT JOIN pos_units u ON u.unit_code = p.basis_unit_code
                WHERE p.product_code IN ({inClause})
                """;
            var rows = await PchilkaCliClient.QueryAsync(sql, ct);
            foreach (var row in rows)
            {
                var code = long.Parse(row[0], CultureInfo.InvariantCulture);
                var name = PchilkaCliClient.Cell(row, 1);
                products[code] = new PchilkaProduct
                {
                    ProductCode = code,
                    Name = string.IsNullOrWhiteSpace(name) ? $"Товар {code}" : name.Trim(),
                    GroupCode = PchilkaCliClient.Cell(row, 2) is { } gc ? long.Parse(gc, CultureInfo.InvariantCulture) : null,
                    GroupName = PchilkaCliClient.Cell(row, 3)?.Trim(),
                    UnitAbbr = PchilkaCliClient.Cell(row, 4)?.Trim(),
                    Vat = ParseDecimal(row.Length > 5 ? row[5] : null) ?? 20m,
                    QtySold30d = qtyByCode.GetValueOrDefault(code),
                    AvgUnitPrice = priceByCode.GetValueOrDefault(code),
                };
            }
        }

        // Barcodes — one row per (product_code, barcode); collect into each product's list.
        {
            var sql = $"""
                SELECT product_code, barcode
                FROM pos_product_barcodes
                WHERE product_code IN ({inClause}) AND (active IS NULL OR active = 1)
                """;
            var rows = await PchilkaCliClient.QueryAsync(sql, ct);
            foreach (var row in rows)
            {
                var code = long.Parse(row[0], CultureInfo.InvariantCulture);
                var barcode = PchilkaCliClient.Cell(row, 1)?.Trim();
                if (string.IsNullOrWhiteSpace(barcode)) continue;
                if (products.TryGetValue(code, out var p))
                    p.Barcodes.Add(barcode);
            }
        }

        return [.. products.Values];
    }

    /// <summary>
    /// Orders + lines for the shop/date window, restricted to lines whose product_code is in
    /// the selected top-N set (non-matching lines are dropped by the JOIN itself — never
    /// fetched). Orders that end up with zero matching lines are simply absent from the
    /// result, satisfying the "skip orders with zero matching lines" requirement for free.
    /// </summary>
    public async Task<List<PchilkaOrder>> GetOrdersAsync(
        int shopCode, DateOnly from, DateOnly to, IReadOnlyCollection<long> productCodes, CancellationToken ct)
    {
        if (productCodes.Count == 0) return [];

        var inClause = string.Join(",", productCodes);
        var sql = $"""
            SELECT o.shop_code, o.workplace_id, o.order_code, o.order_day, o.ordered_at,
                   o.client_code, o.receipt_number, o.order_total,
                   oi.line_number, oi.product_code, oi.quantity, oi.unit_price, oi.line_total, oi.discount_total
            FROM pos_orders o
            JOIN pos_order_items oi
              ON oi.shop_code = o.shop_code AND oi.workplace_id = o.workplace_id AND oi.order_code = o.order_code
            WHERE o.shop_code = {shopCode} AND o.order_day BETWEEN '{Sql(from)}' AND '{Sql(to)}'
              AND oi.quantity > 0 AND oi.product_code IN ({inClause})
            ORDER BY o.workplace_id, o.order_code, oi.line_number
            """;

        var rows = await PchilkaCliClient.QueryAsync(sql, ct);
        var orders = new Dictionary<(int, int, long), PchilkaOrder>();

        foreach (var row in rows)
        {
            var shop = int.Parse(row[0], CultureInfo.InvariantCulture);
            var workplace = int.Parse(row[1], CultureInfo.InvariantCulture);
            var orderCode = long.Parse(row[2], CultureInfo.InvariantCulture);
            var key = (shop, workplace, orderCode);

            if (!orders.TryGetValue(key, out var order))
            {
                order = new PchilkaOrder
                {
                    ShopCode = shop,
                    WorkplaceId = workplace,
                    OrderCode = orderCode,
                    OrderDay = DateOnly.Parse(row[3], CultureInfo.InvariantCulture),
                    OrderedAt = DateTime.Parse(row[4], CultureInfo.InvariantCulture),
                    ClientCode = PchilkaCliClient.Cell(row, 5) is { } cc ? long.Parse(cc, CultureInfo.InvariantCulture) : null,
                    ReceiptNumber = PchilkaCliClient.Cell(row, 6) is { } rn ? long.Parse(rn, CultureInfo.InvariantCulture) : null,
                    OrderTotal = ParseDecimal(row.Length > 7 ? row[7] : null),
                };
                orders[key] = order;
            }

            order.Lines.Add(new PchilkaOrderLine
            {
                LineNumber = int.Parse(row[8], CultureInfo.InvariantCulture),
                ProductCode = long.Parse(row[9], CultureInfo.InvariantCulture),
                Quantity = ParseDecimal(row[10]) ?? 0m,
                UnitPrice = ParseDecimal(row.Length > 11 ? row[11] : null) ?? 0m,
                LineTotal = ParseDecimal(row.Length > 12 ? row[12] : null) ?? 0m,
                DiscountTotal = ParseDecimal(row.Length > 13 ? row[13] : null) ?? 0m,
            });
        }

        return [.. orders.Values];
    }

    private static string Sql(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static decimal? ParseDecimal(string? s) =>
        string.IsNullOrEmpty(s) || s == "NULL" ? null : decimal.Parse(s, CultureInfo.InvariantCulture);
}
