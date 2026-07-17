"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useModules } from "@/features/modules/hooks/useModules";
import { RecipeTable } from "@/features/production/components/RecipeTable";

export default function RecipesPage() {
  const t = useTranslations("Dashboard.production.moduleGate");
  const tPage = useTranslations("Dashboard.production.recipesPage");
  const { data: modulesData } = useModules();
  const isActive = !modulesData || modulesData.modules.includes("production");
  const [showInactive, setShowInactive] = useState(false);

  if (!isActive) {
    return (
      <div
        style={{
          padding: "80px 32px",
          textAlign: "center",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: 16,
        }}
      >
        <div style={{ fontSize: 40 }}>🔒</div>
        <h2 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h2>
        <p style={{ color: "#4B5563", fontSize: 14, maxWidth: 440 }}>
          {t("body")}
        </p>
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Show inactive toggle */}
      <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 16 }}>
        <label
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            color: "#9CA3AF",
            fontSize: 13,
            cursor: "pointer",
          }}
        >
          <input
            type="checkbox"
            checked={showInactive}
            onChange={(e) => setShowInactive(e.target.checked)}
            style={{ cursor: "pointer" }}
          />
          {tPage("showInactive")}
        </label>
      </div>
      <RecipeTable showInactive={showInactive} />
    </div>
  );
}
