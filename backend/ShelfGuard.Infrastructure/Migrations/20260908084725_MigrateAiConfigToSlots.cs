using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MigrateAiConfigToSlots : Migration
    {
        /// <summary>
        /// Data-only migration (managed-AI Phase 4a). Before Phase 4 a tenant had at most one AI
        /// config row in <c>integration_configs</c> with <c>Service</c> = <c>'claude'</c> or
        /// <c>'openai'</c> (the provider name WAS the service). Phase 4 splits the agent into
        /// slots (<c>ai_analyst</c> / <c>ai_assistant</c> / <c>ai_consumer</c>): the existing row
        /// becomes the <c>ai_analyst</c> slot and the provider name moves into the <c>Config</c>
        /// jsonb under <c>provider</c>. Idempotent (WHERE guard), no schema change.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE integration_configs
                SET ""Service""   = 'ai_analyst',
                    ""Config""    = jsonb_set(""Config"", '{provider}', to_jsonb(""Service""), true),
                    ""UpdatedAt"" = now()
                WHERE ""Service"" IN ('claude', 'openai');
            ");
        }

        /// <summary>
        /// Reverses the split: the <c>ai_analyst</c> row goes back to a provider-named service
        /// row and the <c>provider</c> key is dropped from the jsonb. Rows without a
        /// <c>provider</c> key fall back to <c>'claude'</c>.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE integration_configs
                SET ""Service""   = COALESCE(""Config"" ->> 'provider', 'claude'),
                    ""Config""    = ""Config"" - 'provider',
                    ""UpdatedAt"" = now()
                WHERE ""Service"" = 'ai_analyst';
            ");
        }
    }
}
