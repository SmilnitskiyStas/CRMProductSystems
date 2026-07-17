"use client";

// Legacy route — redirect to new /locations/:id/floor-plan
import { useEffect } from "react";
import { useParams, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";

export default function LegacyFloorPlanPage() {
  const t = useTranslations("Dashboard.stores");
  const params = useParams<{ id: string }>();
  const router = useRouter();

  useEffect(() => {
    router.replace(`/locations/${params.id}/floor-plan`);
  }, [params.id, router]);

  return (
    <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
      {t("redirecting")}
    </div>
  );
}
