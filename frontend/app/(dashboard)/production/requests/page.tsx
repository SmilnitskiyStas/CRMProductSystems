"use client";

import { ProductionRequestsPage } from "@/features/production/components/ProductionRequestsPage";

// Production side only: incoming store requests (review, order creation, shipping).
export default function ProductionRequestsRoute() {
  return <ProductionRequestsPage mode="production" />;
}
