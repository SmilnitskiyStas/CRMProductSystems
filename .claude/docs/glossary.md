# Glossary

**Owner:** documentation-writer
**Updated:** 2026-06-03

## Business Terms

**FEFO** — First Expired, First Out. Stock consumption rule: always sell/use the batch with the nearest expiry date first.

**Batch (Партія)** — A specific delivery of a product with a unique expiry date and batch number. One product can have multiple batches with different quantities and expiry dates.

**Expiry status:**
- safe — more than 14 days remaining
- warning — 7–14 days remaining
- critical — 1–6 days remaining
- expired — 0 or fewer days remaining
- sold_out — quantity = 0
- needs_verification — last checked more than 90 days ago

**FEFO consumption** — Taking the batch with the lowest expiry_date where quantity > 0.

**Safety buffer (ББ)** — Reserved minimum quantity for shelf presentation (facing). Not available for sale. If sold, counts as a lost sale.

**MOQ** — Minimum Order Quantity. Cannot order less than this from supplier.

**USQ** — Unit Step Quantity. Order must be a multiple of this after MOQ.

**ADU** — Average Daily Usage. Mean daily consumption over 30/60/90 days of valid sales.

**CDA** — Consumption Driven Algorithm. Buffer calculation method with Green/Yellow/Red zones.

**MTS** — Make to Stock. Always on shelf, regularly ordered.
**MTO** — Make to Order. Special orders only.
**NA** — Not Active. Removed from assortment.
**NM** — Not Managed. Tracked but not ordered automatically.

**RLS** — Row Level Security. PostgreSQL feature for tenant isolation.

**Tenant** — A client company using the ShelfGuard platform.

**Provider** — The ShelfGuard platform owner (super-admin).

**Impersonation** — Provider accessing a client's account for support. Always logged.
