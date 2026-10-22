"use client";

import { ProductionRequestsPage } from "@/features/production/components/ProductionRequestsPage";

// Production side only: incoming store requests (review, order creation, shipping).
export default function ProductionRequestsRoute() {
  return (
    <div style={{ padding: "28px 32px" }}>
      <ProductionRequestsPage mode="production" />
    </div>
  );
}
