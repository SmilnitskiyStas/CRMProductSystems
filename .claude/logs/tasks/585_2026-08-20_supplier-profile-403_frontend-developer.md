# TASK-585: remove profile_management gate from /supplier/profile nav link

**Status:** done

## Change
`frontend/components/layout/Sidebar.tsx`, `buildSupplierNavGroup()`, `/supplier/profile` item:
removed `permission: "profile_management"` (kept `roles: SUPPLIER_ONLY`), added a 2-line
comment matching the existing TASK-318/BUG-019 precedent style, explaining that viewing is
intentionally ungated now that the backend companion fix (TASK-585 backend half) no longer
requires `profile_management` to GET the profile — only to edit/publish it.

```diff
-    { href: "/supplier/profile", label: t("supplierCabinet.profile"),  icon: <Store size={16} />,        roles: SUPPLIER_ONLY, permission: "profile_management" },
+    // Profile viewing (TASK-585) — no permission key: backend no longer requires
+    // profile_management to GET the profile, only to edit/publish it.
+    { href: "/supplier/profile", label: t("supplierCabinet.profile"),  icon: <Store size={16} />,        roles: SUPPLIER_ONLY },
```

No other nav items touched (`/supplier/items` `catalog_management` left as-is).

## Verified
- Filtering logic at Sidebar.tsx lines ~816-829: both permission checks (`effectivePermissions
  && item.permission`, `supplierEffectivePermissions && item.permission`) short-circuit false
  when `item.permission` is undefined and fall through to `return true` — same treatment already
  given to the ungated cooperation-flow items (`/supplier/requests`, `/supplier/orders`, etc.)
  right below. No logic change needed there.
- `npm run lint` — pass, no warnings/errors.
- `npx tsc --noEmit` — pass, no errors.
