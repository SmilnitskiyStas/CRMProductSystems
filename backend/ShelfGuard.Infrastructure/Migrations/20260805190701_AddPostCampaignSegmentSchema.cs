using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <summary>
    /// Фаза 4 marketing-analytics plan (`deep-cooking-nygaard.md` §"Фази 2-4", TASK-471) —
    /// post-campaign audience analysis. Two new tables, both staff-only (no consumer JWT ever
    /// touches either), same posture as <c>price_segment_settings</c> (TASK-419):
    ///
    /// <c>post_campaign_segments</c> — one row per uploaded/analyzed audience. Top-level
    /// tenant-scoped table, real FK to <c>tenants</c> (Restrict) and to <c>users</c> via
    /// <c>CreatedByUserId</c> (Restrict — mirrors <c>user_permission_grants.GrantedByUserId</c>,
    /// the existing non-nullable "staff-authored row references its author" precedent in this
    /// codebase; most other <c>CreatedByUserId</c>/<c>CreatedBy</c> columns here are nullable
    /// with SetNull, but this one is intentionally required — a segment always has an owner).
    /// The nullability of <c>AfterStart</c>/<c>AfterEnd</c>/<c>BeforeStart</c>/<c>BeforeEnd</c>
    /// IS the draft-vs-analyzed state: null means "imported but not yet analyzed", non-null
    /// means "frozen, computed snapshot" that every report tab reads without re-validating the
    /// upload. See <c>PostCampaignSegment</c>'s class remarks for the full rationale.
    ///
    /// <c>post_campaign_segment_members</c> — one row per matched customer within a segment
    /// (never created for unknown/invalid tokens, which stay only as counts + a capped JSONB
    /// sample on the parent). <c>SegmentId</c> -> post_campaign_segments and
    /// <c>CustomerId</c> -> customers are both Cascade (members die with their segment; a hard
    /// customer delete — which this codebase's soft-delete convention makes rare in practice —
    /// takes their segment memberships with it). <c>TenantId</c> here is a plain, indexed,
    /// denormalized column with NO separate FK to <c>tenants</c> — same treatment
    /// <c>loyalty_ledger_entries.TenantId</c> already gets (TASK-404): a child row's tenant
    /// scoping is carried for RLS/query-filter purposes, while the real parent linkage is the
    /// FK to its true parent table.
    ///
    /// Both tables get the canonical fail-closed triad only (tenant_isolation NULLIF-guarded +
    /// provider_bypass `IN ('provider', 'provider_admin')` + worker_bypass) — deliberately NO
    /// identity-based <c>consumer_self_access</c> policy (that pattern is loyalty-specific, for
    /// cross-tenant ConsumerAccount access to their own membership/ledger rows — not applicable
    /// here, since this is a staff-only marketing tool).
    ///
    /// Neither table has any pre-existing data to validate a new FK against (both are brand new,
    /// empty at creation), so the TASK-392/KI-029 "validating ADD CONSTRAINT under RLS" false-
    /// positive-`23503` gotcha does not apply here — that failure mode is specific to adding a
    /// constraint to an *already-populated* column via `ALTER TABLE ... ADD CONSTRAINT`, not to
    /// a brand-new `CREATE TABLE`'s inline FK, which has zero existing rows to check regardless
    /// of which connection role creates it.
    /// </summary>
    public partial class AddPostCampaignSegmentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "post_campaign_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    UploadedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MatchedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UnknownCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    InvalidCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UnknownTokensSample = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    InvalidTokensSample = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    AfterStart = table.Column<DateOnly>(type: "date", nullable: true),
                    AfterEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    BeforeStart = table.Column<DateOnly>(type: "date", nullable: true),
                    BeforeEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    SegmentHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    AnalyzedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_campaign_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_post_campaign_segments_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_post_campaign_segments_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "post_campaign_segment_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_campaign_segment_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_post_campaign_segment_members_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_post_campaign_segment_members_post_campaign_segments_Segmen~",
                        column: x => x.SegmentId,
                        principalTable: "post_campaign_segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_post_campaign_segment_members_tenant_segment",
                table: "post_campaign_segment_members",
                columns: new[] { "TenantId", "SegmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_post_campaign_segment_members_CustomerId",
                table: "post_campaign_segment_members",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "uq_post_campaign_segment_members_segment_customer",
                table: "post_campaign_segment_members",
                columns: new[] { "SegmentId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_post_campaign_segments_tenant_creator",
                table: "post_campaign_segments",
                columns: new[] { "TenantId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_post_campaign_segments_CreatedByUserId",
                table: "post_campaign_segments",
                column: "CreatedByUserId");

            // ── RLS: post_campaign_segments ───────────────────────────────────
            // Canonical triad only — no consumer_self_access (staff-only, see class remarks).
            migrationBuilder.Sql(@"
                ALTER TABLE post_campaign_segments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE post_campaign_segments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON post_campaign_segments
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON post_campaign_segments
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON post_campaign_segments
                  USING (current_setting('app.role', true) = 'worker');
            ");

            // ── RLS: post_campaign_segment_members ────────────────────────────
            // Same canonical triad, direct TenantId column (denormalized, no FK — see class
            // remarks) rather than an EXISTS-through-parent join.
            migrationBuilder.Sql(@"
                ALTER TABLE post_campaign_segment_members ENABLE ROW LEVEL SECURITY;
                ALTER TABLE post_campaign_segment_members FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON post_campaign_segment_members
                  USING (""TenantId"" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
                CREATE POLICY provider_bypass ON post_campaign_segment_members
                  USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
                CREATE POLICY worker_bypass ON post_campaign_segment_members
                  USING (current_setting('app.role', true) = 'worker');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_campaign_segment_members");

            migrationBuilder.DropTable(
                name: "post_campaign_segments");
        }
    }
}
