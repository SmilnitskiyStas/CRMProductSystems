"use client";

import { ProductionRequestsPage } from "@/features/production/components/ProductionRequestsPage";

// Store side: order finished goods from own production (the "Постачання" block).
// Review / ship / summary live on /production/requests.
export default function OrdersProductionRoute() {
  return (
    <div style={{ padding: "28px 32px" }}>
      <ProductionRequestsPage mode="store" />
    </div>
  );
}
