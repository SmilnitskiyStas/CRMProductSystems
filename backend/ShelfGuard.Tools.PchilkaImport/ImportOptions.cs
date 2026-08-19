using Microsoft.Extensions.Configuration;

namespace ShelfGuard.Tools.PchilkaImport;

public sealed record ImportOptions(
    string TenantSlug,
    int ShopCode,
    int TopProductCount,
    DateOnly SalesWindowFrom,
    DateOnly SalesWindowTo,
    DateOnly ImportWindowFrom,
    DateOnly ImportWindowTo)
{
    public static ImportOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("PchilkaImport");
        return new ImportOptions(
            TenantSlug: section["TenantSlug"] ?? "svizhy-kut",
            ShopCode: int.Parse(section["ShopCode"] ?? "33"),
            TopProductCount: int.Parse(section["TopProductCount"] ?? "200"),
            SalesWindowFrom: DateOnly.Parse(section["SalesWindowFrom"]!),
            SalesWindowTo: DateOnly.Parse(section["SalesWindowTo"]!),
            ImportWindowFrom: DateOnly.Parse(section["ImportWindowFrom"]!),
            ImportWindowTo: DateOnly.Parse(section["ImportWindowTo"]!));
    }
}
