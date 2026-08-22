-- Restore users table (dropped accidentally in production)

-- Step 1: Drop all orphaned FK constraints referencing OID 17823 (deleted users table)
DO $$
DECLARE
  r RECORD;
BEGIN
  FOR r IN SELECT conname, conrelid::regclass AS tbl
           FROM pg_constraint
           WHERE confrelid = 17823 AND contype = 'f'
  LOOP
    EXECUTE format('ALTER TABLE %s DROP CONSTRAINT IF EXISTS %I', r.tbl, r.conname);
    RAISE NOTICE 'Dropped FK % from %', r.conname, r.tbl;
  END LOOP;
END $$;

-- Step 2: Clear refresh_tokens (all tokens belong to non-existent users)
TRUNCATE refresh_tokens;

-- Step 3: Create provider_roles table
CREATE TABLE IF NOT EXISTS provider_roles (
  "Id"          uuid NOT NULL DEFAULT gen_random_uuid(),
  "DisplayName" character varying(200) NOT NULL,
  "BaseRole"    character varying(50) NOT NULL,
  "Permissions" text[] NOT NULL DEFAULT '{}',
  "IsSystem"    boolean NOT NULL DEFAULT false,
  "CreatedAt"   timestamp with time zone NOT NULL DEFAULT NOW(),
  CONSTRAINT "PK_provider_roles" PRIMARY KEY ("Id")
);

INSERT INTO provider_roles ("DisplayName", "BaseRole", "Permissions", "IsSystem") VALUES
  ('Адмін провайдера', 'provider_admin',
   ARRAY['team_management','view_clients','manage_clients','service_desk','live_chat','admin_panel','marketplace','analytics','schedule_management'],
   true),
  ('Агент підтримки', 'provider_agent',
   ARRAY['view_clients','service_desk','live_chat'],
   true)
ON CONFLICT DO NOTHING;

-- Step 4: Recreate users table
CREATE TABLE users (
  "Id"             uuid NOT NULL DEFAULT gen_random_uuid(),
  "TenantId"       uuid,
  "Email"          character varying(255) NOT NULL,
  "Phone"          character varying(20),
  "FullName"       character varying(255) NOT NULL,
  "PasswordHash"   character varying(255) NOT NULL,
  "Role"           character varying(50) NOT NULL,
  "StoreId"        uuid,
  "TelegramChatId" character varying(100),
  "PushToken"      text,
  "IsActive"       boolean NOT NULL DEFAULT true,
  "LastActiveAt"   timestamp with time zone,
  "CreatedAt"      timestamp with time zone NOT NULL DEFAULT NOW(),
  "Permissions"    jsonb,
  "InvitedByName"  character varying(255),
  "ProviderRoleId" uuid,
  CONSTRAINT "PK_users" PRIMARY KEY ("Id")
);

-- Step 5: Indexes
CREATE UNIQUE INDEX "IX_users_Email"          ON users ("Email");
CREATE INDEX        "IX_users_TenantId"       ON users ("TenantId");
CREATE INDEX        "IX_users_ProviderRoleId" ON users ("ProviderRoleId");

-- Step 6: FK from users to tenants + provider_roles
ALTER TABLE users
  ADD CONSTRAINT "FK_users_tenants_TenantId"
  FOREIGN KEY ("TenantId") REFERENCES tenants ("Id") ON DELETE RESTRICT;

ALTER TABLE users
  ADD CONSTRAINT "FK_users_provider_roles_ProviderRoleId"
  FOREIGN KEY ("ProviderRoleId") REFERENCES provider_roles ("Id") ON DELETE SET NULL;

-- Step 7: Re-add FKs from other tables to users (NOT VALID skips historical data)
ALTER TABLE refresh_tokens
  ADD CONSTRAINT "FK_refresh_tokens_users_UserId"
  FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE NOT VALID;

ALTER TABLE notification_settings
  ADD CONSTRAINT "FK_notification_settings_users_UserId"
  FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE NOT VALID;

ALTER TABLE discounts
  ADD CONSTRAINT "FK_discounts_users_ApprovedBy"
  FOREIGN KEY ("ApprovedBy") REFERENCES users ("Id") ON DELETE SET NULL NOT VALID;

ALTER TABLE discounts
  ADD CONSTRAINT "FK_discounts_users_CreatedBy"
  FOREIGN KEY ("CreatedBy") REFERENCES users ("Id") ON DELETE RESTRICT NOT VALID;

ALTER TABLE as_work_orders
  ADD CONSTRAINT "FK_as_work_orders_users_MechanicUserId"
  FOREIGN KEY ("MechanicUserId") REFERENCES users ("Id") NOT VALID;

ALTER TABLE telegram_link_codes
  ADD CONSTRAINT "FK_telegram_link_codes_users_UserId"
  FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE NOT VALID;

ALTER TABLE pos_shifts
  ADD CONSTRAINT "FK_pos_shifts_users_CashierId"
  FOREIGN KEY ("CashierId") REFERENCES users ("Id") ON DELETE RESTRICT NOT VALID;

ALTER TABLE production_orders
  ADD CONSTRAINT "FK_production_orders_users_CreatedBy"
  FOREIGN KEY ("CreatedBy") REFERENCES users ("Id") NOT VALID;

ALTER TABLE work_schedules
  ADD CONSTRAINT "FK_work_schedules_users_CreatedBy"
  FOREIGN KEY ("CreatedBy") REFERENCES users ("Id") NOT VALID;

ALTER TABLE schedule_shifts
  ADD CONSTRAINT "FK_schedule_shifts_users_UserId"
  FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE NOT VALID;

ALTER TABLE ticket_comments
  ADD CONSTRAINT "FK_ticket_comments_users_AuthorId"
  FOREIGN KEY ("AuthorId") REFERENCES users ("Id") ON DELETE CASCADE NOT VALID;

ALTER TABLE support_tickets
  ADD CONSTRAINT "FK_support_tickets_users_AssignedTo"
  FOREIGN KEY ("AssignedTo") REFERENCES users ("Id") NOT VALID;

ALTER TABLE support_tickets
  ADD CONSTRAINT "FK_support_tickets_users_CreatedBy"
  FOREIGN KEY ("CreatedBy") REFERENCES users ("Id") ON DELETE RESTRICT NOT VALID;

ALTER TABLE provider_schedule_slots
  ADD CONSTRAINT "FK_provider_schedule_slots_users_UserId"
  FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE NOT VALID;

-- Step 8: RLS (same as FixUsersRlsForAuthFlow migration)
ALTER TABLE users ENABLE ROW LEVEL SECURITY;
ALTER TABLE users FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON users
  USING (current_setting('app.tenant_id', true) IS NULL
         OR "TenantId" = current_setting('app.tenant_id', true)::uuid
         OR "TenantId" IS NULL);

-- Step 9: Insert admin user (password: Admin1234!)
-- BCrypt hash generated via pgcrypto: gen_salt('bf', 12)
INSERT INTO users ("TenantId", "Email", "FullName", "PasswordHash", "Role", "IsActive")
VALUES (
  '2bca2569-b994-4375-afc8-80b388bad7c3',
  'stassmilnitskiy@gmail.com',
  'Адмін',
  '$2a$12$/O0cJHG3BugJExByZ2wKz.Kf7nDPr6VhqbBgBeR0ELDvfGk0Usiaq',
  'provider',
  true
);

-- Step 10: Record migration in history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260621200000_AddProviderRoles', '8.0.10')
ON CONFLICT DO NOTHING;

SELECT 'DONE' AS result, COUNT(*) AS user_count FROM users;
