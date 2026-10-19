# TASK-716 security review (read-only, no code changed)

Reviewed: order tracking, receipt postpone, customer activity/receipts, no-purchase customers, incoming supply, sort/pagination (UkOrdering), OrderCalc/AI cap, settings UI gating.

Findings:
- Medium: MovementRepository.GetIncomingAsync (marketplace branch) has no `o.ClientTenantId == tenantId` filter; relies only on RLS (client OR supplier policy) + it.TenantId. Fix: add explicit filter.
- Low: PostponeAsync has no upper bound on NewExpectedAt (year 9999); no activity-log entry; no row-version (concurrent postpone). 
- Low: GET customers/{id}/activity,/receipts expose full per-customer purchase history; customer PII unmasked is consistent with existing GET customers/{id}.
- Info: displaced XML doc comment above GetActivity in CustomersController (profile-history summary now sits above GetActivity); Settings UI hiding is cosmetic, backend policies intact (PrroSettings AtLeastStoreManager, Integrations *OrCapability).

OK: tenant scoping in tracking/customers/no-purchase; SqlQueryRaw parameterized; masking; sortBy whitelist; page/limit clamps; Collate expression uses constant collation name; no secrets in diff.
