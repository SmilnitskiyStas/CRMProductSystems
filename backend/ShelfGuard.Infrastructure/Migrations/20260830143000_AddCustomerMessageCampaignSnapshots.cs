using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260830143000_AddCustomerMessageCampaignSnapshots")]
public partial class AddCustomerMessageCampaignSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customer_message_campaigns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                AudienceSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AudienceDefinition = table.Column<string>(type: "jsonb", nullable: false),
                Channels = table.Column<List<string>>(type: "jsonb", nullable: false),
                MessengerProvider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                EstimatedRecipients = table.Column<int>(type: "integer", nullable: false),
                ResolvedRecipients = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_message_campaigns", x => x.Id);
                table.ForeignKey("FK_customer_message_campaigns_tenants_TenantId", x => x.TenantId, "tenants", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_customer_message_campaigns_users_CreatedByUserId", x => x.CreatedByUserId, "users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "customer_message_recipients",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_message_recipients", x => x.Id);
                table.ForeignKey("FK_customer_message_recipients_customer_message_campaigns_CampaignId", x => x.CampaignId, "customer_message_campaigns", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_customer_message_recipients_customers_CustomerId", x => x.CustomerId, "customers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_customer_message_campaigns_CreatedByUserId", "customer_message_campaigns", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_customer_message_campaigns_TenantId", "customer_message_campaigns", "TenantId");
        migrationBuilder.CreateIndex("idx_customer_message_campaigns_tenant_created", "customer_message_campaigns", new[] { "TenantId", "CreatedAt" }, descending: new[] { false, true });
        migrationBuilder.CreateIndex("IX_customer_message_recipients_CustomerId", "customer_message_recipients", "CustomerId");
        migrationBuilder.CreateIndex("idx_customer_message_recipients_tenant_customer", "customer_message_recipients", new[] { "TenantId", "CustomerId" });
        migrationBuilder.CreateIndex("IX_customer_message_recipients_CampaignId_CustomerId", "customer_message_recipients", new[] { "CampaignId", "CustomerId" }, unique: true);

        migrationBuilder.Sql("""
            ALTER TABLE customer_message_campaigns ENABLE ROW LEVEL SECURITY;
            ALTER TABLE customer_message_campaigns FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON customer_message_campaigns
              USING ("TenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
            CREATE POLICY provider_bypass ON customer_message_campaigns
              USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
            CREATE POLICY worker_bypass ON customer_message_campaigns
              USING (current_setting('app.role', true) = 'worker');

            ALTER TABLE customer_message_recipients ENABLE ROW LEVEL SECURITY;
            ALTER TABLE customer_message_recipients FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON customer_message_recipients
              USING ("TenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid);
            CREATE POLICY provider_bypass ON customer_message_recipients
              USING (current_setting('app.role', true) IN ('provider', 'provider_admin'));
            CREATE POLICY worker_bypass ON customer_message_recipients
              USING (current_setting('app.role', true) = 'worker');
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_message_recipients");
        migrationBuilder.DropTable(name: "customer_message_campaigns");
    }
}
