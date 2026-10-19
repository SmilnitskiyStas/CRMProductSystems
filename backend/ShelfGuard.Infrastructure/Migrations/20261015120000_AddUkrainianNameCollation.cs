using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Alphabetical ordering of Ukrainian names. The database default (en_US.utf8 on the musl-based
    /// postgres:alpine image) compares by code point: Latin letters sort before Cyrillic and the
    /// Ukrainian letters Є/І/Ї/Ґ land outside the alphabet (e.g. "Ємність" before "Абрикос").
    /// This ICU collation orders by the Ukrainian alphabet and puts Cyrillic ahead of Latin,
    /// so a "sort by name" list starts with "А". Used explicitly in ORDER BY (EF.Functions.Collate).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261015120000_AddUkrainianNameCollation")]
    public partial class AddUkrainianNameCollation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_collation WHERE collname = 'uk_cyrl_first') THEN
        CREATE COLLATION public.uk_cyrl_first (provider = icu, locale = 'uk-u-kr-cyrl-latn', deterministic = true);
    END IF;
END $$;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP COLLATION IF EXISTS public.uk_cyrl_first;");
        }
    }
}
