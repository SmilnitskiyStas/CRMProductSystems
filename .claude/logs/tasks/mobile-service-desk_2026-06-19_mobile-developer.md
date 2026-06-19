# TASK: Mobile Service Desk module
**Date:** 2026-06-19
**Agent:** mobile-developer
**Status:** done

## Summary

Implemented the full Service Desk mobile module: ticket list, ticket detail, create ticket, and add comment flows.

## Files Created

### Feature layer
- `mobile/features/service-desk/types.ts` — TypeScript interfaces: Ticket, TicketDetail, TicketComment, PagedResult, CreateTicketPayload, UpdateTicketPayload, AddCommentPayload
- `mobile/features/service-desk/api.ts` — API functions: getMyTickets, getTickets, getTicket, createTicket, updateTicket, addComment
- `mobile/features/service-desk/hooks/useServiceDesk.ts` — React Query hooks: useMyTickets, useTickets, useTicket, useCreateTicket, useUpdateTicket, useAddComment
- `mobile/features/service-desk/components/TicketCard.tsx` — Ticket list card with number, title, priority badge, status badge, category/date/comment count row
- `mobile/features/service-desk/components/CreateTicketModal.tsx` — pageSheet modal with title, description, category (radio rows), priority (4 chips), submit/cancel
- `mobile/features/service-desk/components/AddCommentModal.tsx` — pageSheet modal with multiline body, isInternal toggle (managers only), submit/cancel

### Screens
- `mobile/app/(app)/service-desk/index.tsx` — List screen: "Підтримка" header, in-screen tabs (Мої/Всі, managers only), FlatList with TicketCard, pull-to-refresh, FAB "+", empty state with chatbubble icon
- `mobile/app/(app)/service-desk/[id].tsx` — Detail screen: #N — Title header, status+priority badges, Details section, Description, Comments list (internal comments hidden for non-managers, orange left-border for managers), + Коментар button, "Змінити статус" Alert sheet (managers only)

### Updated
- `mobile/app/(app)/_layout.tsx` — Added hidden routes: service-desk/index and service-desk/[id]

## Role check
isManager = roles: StoreManager, NetworkManager, EnterpriseAdmin, Provider, ProviderAdmin

## TypeScript
`npx tsc --noEmit` → 0 errors
