# Handoff: TASK-319 backend → frontend

## What's ready
`SupplierChatSessionDto` now has a real `UnreadCount` (int, last field, non-breaking).
Populated by `GET /api/supplier-cabinet/chat/sessions` (supplier side, via
`useSupplierChatSessions()` / `ChatInboxTab`) — use it for the badge on that list and on
the Sidebar «Повідомлення» nav item.

## No equivalent "list my sessions" endpoint for the client side
There is NO client-facing "list my chat sessions across suppliers" endpoint yet — a client
always talks to exactly one supplier at a time via
`GET/POST /api/marketplace/suppliers/{id}/chat/messages`. For the client's
"Написати постачальнику" button badge, derive unread purely client-side from the messages
array already returned by that endpoint: count messages where `senderTenantId !=
myTenantId && !isRead`. No new endpoint is needed for this.

## Messages auto-mark-read on poll
`GetMessagesAsync` (used by both the supplier and client message-fetch endpoints) now
calls `MarkMessagesReadAsync` unconditionally after fetching — every time a party's
existing 3-second poll hits an open thread, the OTHER party's messages in that session
become `IsRead = true` server-side. So once a thread is open/visible on either side, its
unread count will naturally drop to 0 on the next poll tick — no explicit "mark read"
action/endpoint needed from the frontend.

## Files touched (backend)
- `backend/ShelfGuard.Application/Features/Marketplace/Dtos/MarketplaceDtos.cs`
- `backend/ShelfGuard.Domain/Interfaces/ISupplierChatRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/SupplierChatRepository.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/SupplierChatService.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/ISupplierChatService.cs`
- `backend/ShelfGuard.Tests/Marketplace/SupplierChatServiceTests.cs` (+6 tests)

No migration, no controller/route changes — `SupplierCabinetController`'s existing chat
endpoints already return `SupplierChatSessionDto`/`SupplierChatMessageDto` unchanged in
shape aside from the new field.
