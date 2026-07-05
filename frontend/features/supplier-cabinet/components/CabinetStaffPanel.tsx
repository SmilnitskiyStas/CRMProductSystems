"use client";

import { useState } from "react";
import { UserMinus, UserPlus } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useCabinetStaff, useDeactivateCabinetStaff } from "../hooks/useSupplierCabinet";
import { InviteStaffModal } from "./InviteStaffModal";
import type { UserDto } from "@/features/users/types";

function StatusDot({ isActive }: { isActive: boolean }) {
  return (
    <span
      style={{
        display: "inline-block",
        width: 8, height: 8,
        borderRadius: "50%",
        background: isActive ? "#4ADE80" : "#374151",
        marginRight: 6,
        flexShrink: 0,
      }}
    />
  );
}

function StaffRow({ user }: { user: UserDto }) {
  const deactivate = useDeactivateCabinetStaff();

  async function handleDeactivate() {
    if (!confirm(`Деактивувати ${user.fullName}? Вони не зможуть увійти в систему.`)) return;
    await deactivate.mutateAsync(user.id);
  }

  const initials = user.fullName
    .split(" ").slice(0, 2).map((n) => n[0]).join("").toUpperCase();

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 12,
        padding: "12px 14px",
        borderBottom: "1px solid #0F1924",
      }}
    >
      <div
        style={{
          width: 32, height: 32, borderRadius: "50%", flexShrink: 0,
          background: user.isActive
            ? "linear-gradient(135deg, #3B82F6, #6366F1)"
            : "#1F2937",
          display: "flex", alignItems: "center", justifyContent: "center",
          color: "#fff", fontSize: 11, fontWeight: 700,
        }}
      >
        {initials}
      </div>

      <div style={{ minWidth: 0, flex: 1 }}>
        <div style={{
          color: "#E8EDF5", fontSize: 13, fontWeight: 500,
          whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
        }}>
          {user.fullName}
        </div>
        <div style={{
          color: "#4B5563", fontSize: 11,
          whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
        }}>
          {user.email}
        </div>
      </div>

      <div style={{ display: "flex", alignItems: "center", flexShrink: 0 }}>
        <StatusDot isActive={user.isActive} />
        <span style={{ color: user.isActive ? "#4ADE80" : "#6B7280", fontSize: 12 }}>
          {user.isActive ? "Активний" : "Деактив."}
        </span>
      </div>

      {user.isActive && (
        <button
          onClick={handleDeactivate}
          disabled={deactivate.isPending}
          title="Деактивувати"
          style={{
            display: "flex", alignItems: "center", justifyContent: "center",
            width: 28, height: 28,
            background: "transparent",
            border: "1px solid #374151",
            borderRadius: 6,
            color: "#F87171",
            cursor: deactivate.isPending ? "not-allowed" : "pointer",
            opacity: deactivate.isPending ? 0.6 : 1,
            flexShrink: 0,
          }}
        >
          <UserMinus size={14} />
        </button>
      )}
    </div>
  );
}

export function CabinetStaffPanel() {
  const { data: staff, isLoading, isError } = useCabinetStaff();
  const [showInvite, setShowInvite] = useState(false);

  return (
    <div
      style={{
        background: "#111827",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "24px 28px",
        display: "flex",
        flexDirection: "column",
        gap: 16,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>
          Співробітники
        </h2>
        <Btn
          onClick={() => setShowInvite(true)}
          icon={<UserPlus size={14} />}
        >
          Запросити співробітника
        </Btn>
      </div>

      {isLoading ? (
        <div style={{ height: 120, background: "#0D1117", borderRadius: 10 }} />
      ) : isError ? (
        <div style={{ color: "#F87171", fontSize: 13 }}>
          Не вдалося завантажити список співробітників.
        </div>
      ) : !staff || staff.length === 0 ? (
        <div
          style={{
            textAlign: "center",
            padding: "32px 0",
            color: "#4B5563",
            fontSize: 13,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
          }}
        >
          Співробітників ще немає.
        </div>
      ) : (
        <div
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            overflow: "hidden",
          }}
        >
          {staff.map((user) => (
            <StaffRow key={user.id} user={user} />
          ))}
        </div>
      )}

      {showInvite && <InviteStaffModal onClose={() => setShowInvite(false)} />}
    </div>
  );
}
