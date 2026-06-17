# TASK-233 — Mobile: Auto Service screens

**Date:** 2026-06-17
**Agent:** mobile-developer
**Status:** done

## Summary

Implemented all Auto Service mobile screens for TASK-233, mirroring the existing write-offs / transfers patterns.

## Files created

### Feature layer
- `mobile/features/auto-service/types.ts` — WorkOrder, Customer, Vehicle, ServiceCatalogItem, SparePartItem types + STATUS_LABELS / STATUS_COLORS / COMPLETABLE_STATUSES
- `mobile/features/auto-service/api.ts` — all API calls (work-orders, customers, service-catalog, items barcode/search)
- `mobile/features/auto-service/hooks/useAutoService.ts` — React Query hooks (useWorkOrders, useWorkOrder, useCompleteWorkOrder, useAddWorkOrderLine, useCustomers, useCreateCustomer, useServiceCatalog)
- `mobile/features/auto-service/components/WorkOrderStatusBadge.tsx` — colored badge by status
- `mobile/features/auto-service/components/WorkOrderCard.tsx` — list card (brand+model, plate, mechanic, status, total)
- `mobile/features/auto-service/components/AddLineModal.tsx` — modal: type selector (service/part), service catalog list, part search + barcode scan via expo-camera, qty/price/discount inputs
- `mobile/features/auto-service/components/CustomerSheet.tsx` — CustomerSheet (detail bottom sheet) + CreateCustomerModal

### App screens
- `mobile/app/(app)/auto-service/index.tsx` — work order list with filter tabs (All / New / In Progress / Waiting Parts / Done), pull-to-refresh, navigate to detail
- `mobile/app/(app)/auto-service/[id].tsx` — work order detail: vehicle info, status badge, lines list with type icons, total, "Завершити наряд" button with 422 error handling ("Недостатньо запчастин"), barcode scan → AddLineModal prefill
- `mobile/app/(app)/auto-service/customers.tsx` — customer list with client-side search, CustomerSheet bottom sheet on tap, FAB → CreateCustomerModal

### Navigation
- `mobile/app/(app)/_layout.tsx` — added auto-service/index, auto-service/[id], auto-service/customers as hidden stack routes (no tab — bottom tab is full at 6 items)

## Decisions

- Auto Service screens are added as hidden stack routes (href: null), accessible from the dashboard or via deep link, because the bottom tab bar is already at 6 items (Dashboard, Stock, Scan FAB, POS, Receipt, Profile). Adding a 7th would crowd it.
- 422 error for "insufficient parts" uses `err.response.data.itemName` falling back to `err.response.data.message` since the backend spec doesn't define a fixed shape.
- Barcode scan in [id].tsx and AddLineModal both use expo-camera CameraView with cssInterop registration (NativeWind pattern from scan.tsx).
- Customer vehicle list is rendered as a count only (backend returns `vehicleCount`; full vehicle list API requires a separate endpoint not in TASK-233 scope).

## Acceptance criteria

1. `tsc --noEmit` — green ✅
2. Work order list screen — FlatList, filter tabs, pull-to-refresh ✅
3. Work order detail — "Завершити наряд" + 422 error handling ✅
4. Barcode scan → AddLineModal prefill (spare_part only) ✅
5. Customer list + CreateCustomerModal ✅
6. Task log ✅ (this file)
7. Backlog updated ✅
