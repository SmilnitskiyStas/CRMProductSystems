"use client";

// Sidebar entry point: resolves the first location and forwards to its floor plan.
import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useLocations } from "@/features/locations/hooks/useLocations";

export default function FloorPlanIndexPage() {
  const t = useTranslations("Dashboard.locations.floorPlanIndex");
  const tCommon = useTranslations("Common");
  const router = useRouter();
  const { data: locations, isLoading } = useLocations();

  useEffect(() => {
    if (locations && locations.length > 0) {
      router.replace(`/locations/${locations[0].id}/floor-plan`);
    }
  }, [locations, router]);

  return (
    <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
      {isLoading ? tCommon("loading") : locations?.length === 0 ? t("noLocations") : t("redirecting")}
    </div>
  );
}
