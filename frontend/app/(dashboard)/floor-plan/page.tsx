"use client";

// Sidebar entry point: resolves the first store and forwards to its floor plan.
import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useStores } from "@/features/stores/hooks/useStores";

export default function FloorPlanIndexPage() {
  const router = useRouter();
  const { data: stores, isLoading } = useStores();

  useEffect(() => {
    if (stores && stores.length > 0) {
      router.replace(`/stores/${stores[0].id}/floor-plan`);
    }
  }, [stores, router]);

  return (
    <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
      {isLoading ? "Завантаження…" : stores?.length === 0 ? "Немає магазинів" : "Перенаправлення…"}
    </div>
  );
}
