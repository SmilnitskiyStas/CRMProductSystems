using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Forgot/reset-password flow (TASK-455) — schema only, no service/controller yet (TASK-456).
    /// New <c>password_reset_tokens</c> table, same "single active token per user" shape as
    /// <c>telegram_link_codes</c> but styled like <c>RefreshToken</c> (private setters, `Create()`
    /// factory, computed `IsActive`, `MarkUsed()`).
    ///
    /// No own TenantId column — tenant is derived transitively through UserId -> users.TenantId,
    /// same as refresh_tokens/telegram_link_codes. `tenant_isolation` is therefore an
    /// EXISTS-through-users join, and deliberately keeps the fail-open
    /// `NULLIF(...) IS NULL OR ...` branch — the third documented exception alongside
    /// `users`/`refresh_tokens` (see `.claude/docs/database-schema.md`): an anonymous
    /// forgot/reset-password request must find the token/user row before
    /// TenantConnectionInterceptor has anything to SET (it only RESETs session vars for
    /// unauthenticated connections). Verified byte-for-byte against refresh_tokens' current live
    /// policy (20260629010000_FixAllRlsPoliciesNullIfEmptyString) before writing this one — same
    /// shape, only the table/column name differs.
    ///
    /// `provider_bypass` is written as IN ('provider', 'provider_admin') from day one, matching
    /// every tenant-scoped table created since 20260714150000_ExpandProviderBypassToProviderAdmin
    /// (the `RLS Template` section of database-schema.md still shows the older single-value form —
    /// that template has not been revisited since the provider_admin expansion; this migration's
    /// SQL is the current source of truth, not that template).
    /// </summary>
    public partial class AddPasswordResetTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_password_reset_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_password_reset_tokens_user",
                table: "password_reset_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_TokenHash",
                table: "password_reset_tokens",
                column: "TokenHash",
                unique: true);

            // ── RLS: password_reset_tokens ────────────────────────────────────
            // Fail-open EXISTS-through-users — deliberate, see class remarks above. Do NOT
            // "fix" this to a fail-closed direct-column form; it would break the anonymous
            // forgot/reset-password lookup the same way it would break login/refresh.
            migrationBuilder.Sql(@"
                ALTER TABLE password_reset_tokens ENABLE ROW LEVEL SECURITY;
                ALTER TABLE password_reset_tokens FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON password_reset_tokens USING (
                  NULLIF(current_setting('app.tenant_id', true), '') IS NULL
                  OR EXISTS (
                    SELECT 1 FROM users u WHERE u.""Id"" = password_reset_tokens.""UserId""
                      AND ((u.""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid)
                           OR u.""TenantId"" IS NULL)
                  )
                );
                CREATE POLICY provider_bypass ON password_reset_tokens
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON password_reset_tokens
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_tokens");
        }
    }
}
