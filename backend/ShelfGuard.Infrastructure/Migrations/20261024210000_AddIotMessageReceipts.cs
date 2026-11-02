using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations;

public partial class AddIotMessageReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "iot_message_receipts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                MessageKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table => table.PrimaryKey("PK_iot_message_receipts", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_iot_message_receipts_TenantId_DeviceId_MessageKey",
            table: "iot_message_receipts",
            columns: new[] { "TenantId", "DeviceId", "MessageKey" }, unique: true);
        migrationBuilder.Sql("""
            ALTER TABLE iot_message_receipts ENABLE ROW LEVEL SECURITY;
            ALTER TABLE iot_message_receipts FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON iot_message_receipts
              USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
              WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            CREATE POLICY worker_bypass ON iot_message_receipts
              USING (current_setting('app.role', true) = 'worker')
              WITH CHECK (current_setting('app.role', true) = 'worker');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retain IoT ingestion keys to prevent replay. Roll back the worker without dropping this table.");
}
