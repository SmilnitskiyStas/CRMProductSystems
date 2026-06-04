# Skill: Create Schema

Source of truth: v1-spec.md section 4 (full SQL schema)

Pattern:
- UUID PKs: gen_random_uuid()
- tenant_id on every data table
- is_active for soft delete
- created_at TIMESTAMPTZ DEFAULT NOW()
- FEFO tables need expiry_date DATE NOT NULL

Mandatory for every tenant table:
ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON {table} USING (tenant_id = current_setting('app.tenant_id')::uuid);
CREATE POLICY provider_bypass ON {table} USING (current_setting('app.role') = 'provider');
