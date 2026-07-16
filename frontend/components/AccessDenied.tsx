"use client";

import { useTranslations } from "next-intl";
import { ShieldOff } from "lucide-react";

interface AccessDeniedProps {
  title?: string;
}

export function AccessDenied({ title }: AccessDeniedProps) {
  const t = useTranslations("Dashboard.ui.accessDenied");
  return (
    <div
      style={{
        padding: 40,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: 16,
        minHeight: 320,
        color: "#4B5563",
        textAlign: "center",
      }}
    >
      <ShieldOff size={40} strokeWidth={1.5} />
      {title && (
        <h1 style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, margin: 0 }}>{title}</h1>
      )}
      <p style={{ fontSize: 13, margin: 0, maxWidth: 340 }}>
        {t("body")}
      </p>
    </div>
  );
}
