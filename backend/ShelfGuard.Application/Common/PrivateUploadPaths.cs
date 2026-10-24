namespace ShelfGuard.Application.Common;

/// <summary>Private uploads keep their stored logical URLs; only authenticated APIs resolve them.</summary>
public static class PrivateUploadPaths
{
    public const string Root = "private-uploads";
    public static readonly string[] Categories = ["contracts", "contract-assets", "shelf-scans", "floor-plans"];

    public static bool TryParse(string url, out string category, out Guid tenantId, out string fileName)
    {
        category = fileName = string.Empty;
        tenantId = Guid.Empty;
        var parts = url.Split('/');
        if (parts.Length != 5 || parts[0] != "" || parts[1] != "uploads"
            || !Categories.Contains(parts[2]) || !Guid.TryParseExact(parts[3], "D", out tenantId))
            return false;
        var name = parts[4];
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.Length == 0 || stem.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            || !new[] { ".jpg", ".jpeg", ".png", ".webp", ".pdf" }.Contains(extension))
            return false;
        category = parts[2];
        fileName = name;
        return true;
    }

    public static string? GetReadPath(string url, string root = Root, string legacyRoot = "wwwroot/uploads")
    {
        if (!TryParse(url, out var category, out var tenantId, out var fileName)) return null;
        var relative = Path.Combine(category, tenantId.ToString(), fileName);
        var privatePath = Path.Combine(root, relative);
        // Legacy files remain readable only through authorized code during migration.
        return File.Exists(privatePath) ? privatePath : Path.Combine(legacyRoot, relative);
    }
}
