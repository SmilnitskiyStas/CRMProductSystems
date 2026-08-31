using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.Domain;

/// <summary>
/// TASK-648: Ukraine region registry (ISO 3166-2:UA oblasts + major cities), the backend
/// source of truth behind <c>GET /api/geo/regions</c>. Mirrors <see cref="SupplierItemCategoriesTests"/>.
/// </summary>
public sealed class UkraineRegionsTests
{
    [Fact]
    public void All_Contains27Oblasts()
    {
        var oblasts = UkraineRegions.All.Where(r => r.Kind == UkraineRegions.KindOblast).ToList();

        Assert.Equal(27, oblasts.Count);
        Assert.All(oblasts, o => Assert.Null(o.ParentCode));
        Assert.All(oblasts, o => Assert.Matches(@"^UA-\d{2}$", o.Code));
    }

    [Fact]
    public void All_CodesAreUnique()
    {
        var codes = UkraineRegions.All.Select(r => r.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void All_NamesAreNonEmpty()
    {
        Assert.All(UkraineRegions.All, r => Assert.False(string.IsNullOrWhiteSpace(r.NameUa)));
    }

    [Fact]
    public void All_EveryCity_HasValidOblastParent()
    {
        var cities = UkraineRegions.All.Where(r => r.Kind == UkraineRegions.KindCity).ToList();

        Assert.NotEmpty(cities);
        Assert.All(cities, c =>
        {
            Assert.NotNull(c.ParentCode);
            var parent = UkraineRegions.Find(c.ParentCode!);
            Assert.NotNull(parent);
            Assert.Equal(UkraineRegions.KindOblast, parent!.Kind);
            Assert.StartsWith(c.ParentCode + "-", c.Code);
        });
    }

    [Fact]
    public void All_CityCodesAreAsciiUppercase()
    {
        var cities = UkraineRegions.All.Where(r => r.Kind == UkraineRegions.KindCity);

        Assert.All(cities, c => Assert.Matches(@"^UA-\d{2}-[A-Z-]+$", c.Code));
    }

    [Fact]
    public void Kyiv_CityAndOblast_AreDistinct()
    {
        var cityOfKyiv = UkraineRegions.Find("UA-30");
        var kyivOblast = UkraineRegions.Find("UA-32");

        Assert.NotNull(cityOfKyiv);
        Assert.NotNull(kyivOblast);
        Assert.Equal("м. Київ", cityOfKyiv!.NameUa);
        Assert.Equal("Київська", kyivOblast!.NameUa);
        // UA-30 has no separate city row
        Assert.DoesNotContain(UkraineRegions.All, r => r.ParentCode == "UA-30");
    }

    [Fact]
    public void CrimeaAndSevastopol_ArePresentAsNeutralOblasts()
    {
        Assert.Equal("Автономна Республіка Крим", UkraineRegions.Find("UA-43")!.NameUa);
        Assert.Equal("Севастополь", UkraineRegions.Find("UA-40")!.NameUa);
    }

    [Theory]
    [InlineData("UA-05")]
    [InlineData("UA-32")]
    [InlineData("UA-18-ZHYTOMYR")]
    [InlineData("UA-46-LVIV")]
    public void FindAndIsValid_RoundTrip_ForKnownCodes(string code)
    {
        Assert.True(UkraineRegions.IsValid(code));
        var def = UkraineRegions.Find(code);
        Assert.NotNull(def);
        Assert.Equal(code, def!.Code);
    }

    [Theory]
    [InlineData("UA-99")]
    [InlineData("ua-05")]
    [InlineData("")]
    [InlineData("Kyiv")]
    public void FindAndIsValid_RejectUnknownCodes(string code)
    {
        Assert.False(UkraineRegions.IsValid(code));
        Assert.Null(UkraineRegions.Find(code));
    }

    [Fact]
    public void Validate_AllKnown_ReturnsEmpty()
    {
        Assert.Empty(UkraineRegions.Validate(new[] { "UA-05", "UA-32", "UA-46-LVIV" }));
    }

    [Fact]
    public void Validate_NullInput_ReturnsEmpty()
    {
        Assert.Empty(UkraineRegions.Validate(null));
    }

    [Fact]
    public void Validate_UnknownAndBlankCodes_ReturnOneErrorEach()
    {
        var errors = UkraineRegions.Validate(new[] { "UA-05", "UA-999", "  " });

        Assert.Equal(2, errors.Count);
    }

    [Theory]
    [InlineData("Київська область", "UA-32")]
    [InlineData("Київська обл.", "UA-32")]
    [InlineData("київська", "UA-32")]
    [InlineData("м. Київ", "UA-30")]
    [InlineData("місто Київ", "UA-30")]
    [InlineData("Київ", "UA-30")]
    [InlineData("Одеська область", "UA-51")]
    [InlineData("  ЖИТОМИРСЬКА  ", "UA-18")]
    [InlineData("Дніпро", "UA-12")]
    [InlineData("Дніпропетровськ", "UA-12")]
    [InlineData("АР Крим", "UA-43")]
    [InlineData("крим", "UA-43")]
    [InlineData("Автономна Республіка Крим", "UA-43")]
    [InlineData("Львів", "UA-46-LVIV")]
    [InlineData("UA-63", "UA-63")]
    public void TryMatchFreeText_MapsKnownForms(string raw, string expected)
    {
        Assert.Equal(expected, UkraineRegions.TryMatchFreeText(raw));
    }

    [Theory]
    [InlineData("Вся Україна")]
    [InlineData("за домовленістю")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Warszawa")]
    public void TryMatchFreeText_ReturnsNull_WhenNothingMatches(string raw)
    {
        Assert.Null(UkraineRegions.TryMatchFreeText(raw));
    }
}
