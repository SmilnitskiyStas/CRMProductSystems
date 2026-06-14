"use client";

import { useQueryClient } from "@tanstack/react-query";
import { LogOut } from "lucide-react";
import { setToken, clearToken } from "@/lib/api";
import { providerApi } from "../api/provider";
import { ME_KEY } from "@/features/auth/hooks/useAuth";

interface Props {
  tenantName: string;
  tenantId: string;
  onExit: () => void;
}

export function ImpersonationBanner({ tenantName, tenantId, onExit }: Props) {
  const queryClient = useQueryClient();

  async function handleExit() {
    try {
      await providerApi.endImpersonate(tenantId);
    } catch {
      // Ignore — server just logs; we restore the token regardless
    }

    if (typeof window !== "undefined") {
      const original = sessionStorage.getItem("sg_provider_token");
      if (original) {
        setToken(original);
        sessionStorage.removeItem("sg_provider_token");
      } else {
        clearToken();
      }
      sessionStorage.removeItem("sg_impersonation");
      window.dispatchEvent(new Event("sg-impersonation-changed"));
    }

    // Refresh useMe() so Sidebar reflects the restored provider role
    await queryClient.invalidateQueries({ queryKey: ME_KEY });
    onExit();
  }

  return (
    <div
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        zIndex: 100,
        background: "linear-gradient(90deg, #1D3461, #2D1B69)",
        borderBottom: "1px solid #3B82F6",
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "10px 24px",
        gap: 16,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <div
          style={{
            width: 8, height: 8,
            borderRadius: "50%",
            background: "#60A5FA",
            flexShrink: 0,
            animation: "pulse 2s infinite",
          }}
        />
        <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
          Режим перегляду:
        </span>
        <span style={{ color: "#93C5FD", fontSize: 13, fontWeight: 700 }}>
          {tenantName}
        </span>
        <span style={{ color: "#4B5563", fontSize: 12 }}>
          (enterprise_admin · 60 хв)
        </span>
      </div>

      <button
        onClick={handleExit}
        style={{
          display: "flex",
          alignItems: "center",
          gap: 6,
          padding: "6px 14px",
          borderRadius: 7,
          background: "rgba(239, 68, 68, 0.15)",
          border: "1px solid #EF4444",
          color: "#F87171",
          fontSize: 12,
          fontWeight: 600,
          cursor: "pointer",
        }}
      >
        <LogOut size={13} />
        Вийти з перегляду
      </button>
    </div>
  );
}
