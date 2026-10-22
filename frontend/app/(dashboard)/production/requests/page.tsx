"use client";

import { ProductionRequestsPage } from "@/features/production/components/ProductionRequestsPage";

// Not module-gated on the page itself: stores raise requests; production-only actions
// (summary, review, ship, create orders) are enabled inside by the tenant's modules.
export default function ProductionRequestsRoute() {
  return (
    <div style={{ padding: "28px 32px" }}>
      <ProductionRequestsPage />
    </div>
  );
}
