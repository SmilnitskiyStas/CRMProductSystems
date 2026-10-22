"use client";

import { ProductionRequestsPage } from "@/features/production/components/ProductionRequestsPage";

// Store side: order finished goods from own production (the "Постачання" block).
// Review / ship / summary live on /production/requests.
export default function OrdersProductionRoute() {
  return <ProductionRequestsPage mode="store" />;
}
