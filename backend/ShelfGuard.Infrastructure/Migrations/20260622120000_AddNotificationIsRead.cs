using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260622120000_AddNotificationIsRead")]
    public partial class AddNotificationIsRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: this migration was originally applied to prod out-of-band
            // (missing [Migration] attribute), so it may re-run on an already-migrated DB.
            migrationBuilder.Sql(@"
ALTER TABLE notification_queue ADD COLUMN IF NOT EXISTS ""IsRead"" boolean NOT NULL DEFAULT false;
ALTER TABLE notification_queue ADD COLUMN IF NOT EXISTS ""ReadAt"" timestamp with time zone;
CREATE INDEX IF NOT EXISTS idx_notification_queue_tenant_unread
    ON notification_queue (""TenantId"", ""IsRead"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_notification_queue_tenant_unread",
                table: "notification_queue");

            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "notification_queue");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "notification_queue");
        }
    }
}
