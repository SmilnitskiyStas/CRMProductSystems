"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { UserPlus, Trash2, Users, Pencil, RefreshCw } from "lucide-react";
import { useProviderTeam, useDeactivateMember, useReactivateMember } from "../hooks/useProviderTeam";
import { InviteProviderMemberModal } from "./InviteProviderMemberModal";
import { EditMemberModal } from "./EditMemberModal";
import type { ProviderTeamMemberDto } from "../api/providerTeamApi";
import { Btn } from "@/components/ui/Btn";
import { StatusPill } from "@/components/ui/StatusPill";

const ROLE_COLORS: Record<string, string> = {
  provider:       "#A78BFA",
  provider_admin: "#60A5FA",
  provider_agent: "#4ADE80",
};

export function TeamTab() {
  const t = useTranslations("Dashboard.provider.teamTab");
  const tRoleLabels = useTranslations("Dashboard.provider.roleLabels");
  const { data: members, isLoading } = useProviderTeam();
  const deactivate  = useDeactivateMember();
  const reactivate  = useReactivateMember();
  const [showInvite, setShowInvite] = useState(false);
  const [editMember, setEditMember] = useState<ProviderTeamMemberDto | null>(null);

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <Users size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
            {t("title")}
          </h2>
          {members && (
            <StatusPill bg="#1F2937" text="#9CA3AF">
              {members.length}
            </StatusPill>
          )}
        </div>
        <Btn icon={<UserPlus size={14} />} onClick={() => setShowInvite(true)}>
          {t("inviteButton")}
        </Btn>
      </div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 14 }}>{t("loading")}</div>
      ) : !members?.length ? (
        <div style={{ color: "#4B5563", fontSize: 14 }}>{t("empty")}</div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {members.map((m) => (
            <div
              key={m.id}
              style={{
                display: "flex", alignItems: "center", justifyContent: "space-between",
                background: "#111827", border: "1px solid #1F2937",
                borderRadius: 10, padding: "12px 16px",
                opacity: m.isActive ? 1 : 0.55,
              }}
            >
              <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 500 }}>{m.fullName}</span>
                <span style={{ color: "#4B5563", fontSize: 12 }}>{m.email}</span>
              </div>
              <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <StatusPill bg={`${ROLE_COLORS[m.role] ?? "#6B7280"}22`} text={ROLE_COLORS[m.role] ?? "#6B7280"}>
                  {tRoleLabels.has(m.role) ? tRoleLabels(m.role) : m.role}
                </StatusPill>

                {/* Edit button — always visible for active members */}
                {m.isActive && (
                  <button
                    onClick={() => setEditMember(m)}
                    title={t("editTitle")}
                    style={{
                      background: "transparent", border: "none",
                      color: "#4B5563", cursor: "pointer", padding: 4,
                      transition: "color 0.15s",
                    }}
                    onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#60A5FA"; }}
                    onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
                  >
                    <Pencil size={14} />
                  </button>
                )}

                {/* Deactivate — only non-owner active members */}
                {m.isActive && m.role !== "provider" && (
                  <button
                    onClick={() => {
                      if (confirm(t("confirmDeactivate", { name: m.fullName }))) {
                        deactivate.mutate(m.id);
                      }
                    }}
                    title={t("deactivateTitle")}
                    style={{
                      background: "transparent", border: "none",
                      color: "#4B5563", cursor: "pointer", padding: 4,
                      transition: "color 0.15s",
                    }}
                    onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#F87171"; }}
                    onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
                  >
                    <Trash2 size={14} />
                  </button>
                )}

                {/* Reactivate — for inactive members */}
                {!m.isActive && (
                  <>
                    <span style={{ color: "#4B5563", fontSize: 11 }}>{t("inactiveLabel")}</span>
                    <Btn
                      variant="ghost"
                      size="xs"
                      title={t("reactivateTitle")}
                      icon={<RefreshCw size={11} />}
                      onClick={() => {
                        if (confirm(t("confirmReactivate", { name: m.fullName }))) {
                          reactivate.mutate(m.id);
                        }
                      }}
                    >
                      {t("restoreButton")}
                    </Btn>
                  </>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {showInvite && <InviteProviderMemberModal onClose={() => setShowInvite(false)} />}
      {editMember && <EditMemberModal member={editMember} onClose={() => setEditMember(null)} />}
    </div>
  );
}
