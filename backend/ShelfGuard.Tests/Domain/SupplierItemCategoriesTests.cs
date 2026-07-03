using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.Domain;

/// <summary>
/// TASK-294 (ADR-017 §4/§5): fixed category/field registry and required-field validation.
/// </summary>
public sealed class SupplierItemCategoriesTests
{
    [Fact]
    public void Validate_NullCategory_AlwaysValid_RegardlessOfAttributes()
    {
        Assert.Empty(SupplierItemCategories.Validate(null, null));
        Assert.Empty(SupplierItemCategories.Validate(null, new Dictionary<string, object?>()));
        Assert.Empty(SupplierItemCategories.Validate(null, new Dictionary<string, object?> { ["garbage"] = "x" }));
    }

    [Fact]
    public void Validate_UnknownCategory_ReturnsError()
    {
        var errors = SupplierItemCategories.Validate("textiles", null);

        Assert.Single(errors);
    }

    [Fact]
    public void Validate_Food_MissingRequiredFields_ReturnsErrors()
    {
        var errors = SupplierItemCategories.Validate("food", new Dictionary<string, object?>());

        Assert.Contains(errors, e => e.Contains("Вага/об'єм"));
        Assert.Contains(errors, e => e.Contains("Термін придатності"));
        // batch_number is optional — no error expected for it
        Assert.DoesNotContain(errors, e => e.Contains("Номер партії"));
    }

    [Fact]
    public void Validate_Food_AllRequiredPresent_ReturnsEmpty()
    {
        var attrs = new Dictionary<string, object?>
        {
            ["weight_volume"] = "1 кг",
            ["expiry_date"]   = "2026-12-31",
        };

        Assert.Empty(SupplierItemCategories.Validate("food", attrs));
    }

    [Fact]
    public void Validate_AutoParts_MissingOemNumber_ReturnsError()
    {
        var errors = SupplierItemCategories.Validate("auto_parts", new Dictionary<string, object?>());

        Assert.Single(errors);
        Assert.Contains("OEM-номер", errors[0]);
    }

    [Fact]
    public void Validate_AutoParts_AllRequiredPresent_ReturnsEmpty()
    {
        var attrs = new Dictionary<string, object?> { ["oem_number"] = "12345-ABC" };

        Assert.Empty(SupplierItemCategories.Validate("auto_parts", attrs));
    }

    [Fact]
    public void Validate_Medical_MissingRequiredFields_ReturnsErrors()
    {
        var errors = SupplierItemCategories.Validate("medical", new Dictionary<string, object?>());

        Assert.Equal(3, errors.Count); // dosage, expiry_date, prescription_status
        Assert.Contains(errors, e => e.Contains("Дозування"));
        Assert.Contains(errors, e => e.Contains("Термін придатності"));
        Assert.Contains(errors, e => e.Contains("Рецептурний статус"));
    }

    [Fact]
    public void Validate_Medical_AllRequiredPresent_ReturnsEmpty()
    {
        var attrs = new Dictionary<string, object?>
        {
            ["dosage"]               = "500 мг",
            ["expiry_date"]          = "2027-01-01",
            ["prescription_status"]  = "ОТС",
        };

        Assert.Empty(SupplierItemCategories.Validate("medical", attrs));
    }

    [Fact]
    public void Validate_Construction_MissingUnit_ReturnsError()
    {
        var errors = SupplierItemCategories.Validate("construction", new Dictionary<string, object?>());

        Assert.Single(errors);
        Assert.Contains("Одиниця виміру", errors[0]);
    }

    [Fact]
    public void Validate_Construction_AllRequiredPresent_ReturnsEmpty()
    {
        var attrs = new Dictionary<string, object?> { ["unit"] = "кг" };

        Assert.Empty(SupplierItemCategories.Validate("construction", attrs));
    }

    [Fact]
    public void Validate_NullAttributes_KnownCategory_ReturnsErrorsForAllRequiredFields()
    {
        var errors = SupplierItemCategories.Validate("food", null);

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Validate_BlankStringValue_TreatedAsMissing()
    {
        var attrs = new Dictionary<string, object?> { ["oem_number"] = "   " };

        var errors = SupplierItemCategories.Validate("auto_parts", attrs);

        Assert.Single(errors);
    }

    [Fact]
    public void All_ContainsExactlyFourCategories()
    {
        Assert.Equal(4, SupplierItemCategories.All.Count);
        Assert.Contains(SupplierItemCategories.All, c => c.Key == "food");
        Assert.Contains(SupplierItemCategories.All, c => c.Key == "auto_parts");
        Assert.Contains(SupplierItemCategories.All, c => c.Key == "medical");
        Assert.Contains(SupplierItemCategories.All, c => c.Key == "construction");
    }
}
