using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShelfGuard.Infrastructure.Data;

#nullable disable

namespace ShelfGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260621200000_AddProviderRoles")]
    public partial class AddProviderRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: this migration was originally applied to prod out-of-band
            // (missing [Migration] attribute), so it may re-run on an already-migrated DB.
            migrationBuilder.Sql(@"
DO $mig$
BEGIN
    IF to_regclass('public.provider_roles') IS NULL THEN
        CREATE TABLE provider_roles (
            ""Id""          uuid NOT NULL DEFAULT gen_random_uuid(),
            ""DisplayName"" character varying(200) NOT NULL,
            ""BaseRole""    character varying(50) NOT NULL,
            ""Permissions"" text[] NOT NULL,
            ""IsSystem""    boolean NOT NULL DEFAULT false,
            ""CreatedAt""   timestamp with time zone NOT NULL DEFAULT NOW(),
            CONSTRAINT ""PK_provider_roles"" PRIMARY KEY (""Id"")
        );

        -- Seed system built-in roles (only on first creation)
        INSERT INTO provider_roles (""Id"", ""DisplayName"", ""BaseRole"", ""Permissions"", ""IsSystem"") VALUES
          (gen_random_uuid(), 'Адмін провайдера', 'provider_admin',
           ARRAY['team_management','view_clients','manage_clients','service_desk','live_chat','admin_panel','marketplace','analytics','schedule_management'],
           true),
          (gen_random_uuid(), 'Агент підтримки', 'provider_agent',
           ARRAY['view_clients','service_desk','live_chat'],
           true);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'users' AND column_name = 'ProviderRoleId'
    ) THEN
        ALTER TABLE users ADD COLUMN ""ProviderRoleId"" uuid;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'FK_users_provider_roles_ProviderRoleId'
    ) THEN
        ALTER TABLE users ADD CONSTRAINT ""FK_users_provider_roles_ProviderRoleId""
            FOREIGN KEY (""ProviderRoleId"") REFERENCES provider_roles(""Id"") ON DELETE SET NULL;
    END IF;
END $mig$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_provider_roles_ProviderRoleId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "ProviderRoleId",
                table: "users");

            migrationBuilder.DropTable(
                name: "provider_roles");
        }
    }
}
