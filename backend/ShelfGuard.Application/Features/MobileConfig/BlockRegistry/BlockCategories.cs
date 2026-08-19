namespace ShelfGuard.Application.Features.MobileConfig.BlockRegistry;

/// <summary>
/// Groupings shown in the App Builder's block palette (CLAUDE CODE SPEC §11's "BLOCKS" column —
/// Banner / Loyalty / Promotions / Products / News). <see cref="Layout"/> and <see cref="Stores"/>
/// cover the two Core Blocks V1 types (<c>sectionHeader</c>/<c>quickActions</c>, <c>storeList</c>)
/// the spec's illustrative palette didn't name a category for.
/// </summary>
public static class BlockCategories
{
    public const string Banner = "banner";
    public const string Loyalty = "loyalty";
    public const string Promotions = "promotions";
    public const string Products = "products";
    public const string News = "news";
    public const string Stores = "stores";
    public const string Layout = "layout";
}
