"use client";

import type { LucideIcon } from "lucide-react";
import { useTranslations } from "next-intl";

interface PlaceholderSectionProps {
  icon: LucideIcon;
}

/**
 * TASK-535: shared "not yet built" state for the new Retailer Admin shell routes
 * (/consumer-app/design, /pages, /navigation, /features, /versions). Each of those
 * routes is scaffolding only — the actual builder UI lands in later tasks (TASK-536+).
 * Visually distinct from AccessDenied (frontend/components/AccessDenied.tsx, which this
 * mirrors color/typography-wise) via a dashed border, so it reads as "planned" rather
 * than "forbidden". Copy comes from the shared Dashboard.consumerApp.placeholder i18n key
 * — same heading/body across every placeholder route, page title/subtitle already carry
 * the section-specific description.
 */
export function PlaceholderSection({ icon: Icon }: PlaceholderSectionProps) {
  const t = useTranslations("Dashboard.consumerApp.placeholder");

  return (
    <div
      style={{
        padding: 40,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: 16,
        minHeight: 280,
        color: "#4B5563",
        textAlign: "center",
        border: "1px dashed #2A3441",
        borderRadius: 12,
      }}
    >
      <Icon size={40} strokeWidth={1.5} />
      <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
        {t("heading")}
      </h2>
      <p style={{ fontSize: 13, margin: 0, maxWidth: 380 }}>{t("body")}</p>
    </div>
  );
}
