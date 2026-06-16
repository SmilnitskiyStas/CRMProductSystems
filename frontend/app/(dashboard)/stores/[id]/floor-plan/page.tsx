"use client";

// Legacy route — redirect to new /locations/:id/floor-plan
import { useEffect } from "react";
import { useParams, useRouter } from "next/navigation";

export default function LegacyFloorPlanPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();

  useEffect(() => {
    router.replace(`/locations/${params.id}/floor-plan`);
  }, [params.id, router]);

  return (
    <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
      Перенаправлення…
    </div>
  );
}
