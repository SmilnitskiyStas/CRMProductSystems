"use client";

// Client chooses how to sign an awaiting_signature cooperation agreement:
// "physical" (print/sign/mail) or "vchasno" (e-signature by email) — TASK-319/320.

import { useState } from "react";
import { FileSignature, Send } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useChooseSigningMethod } from "../hooks/useCooperation";
import type { CooperationAgreementDto } from "../types";

interface Props {
  agreement: CooperationAgreementDto;
}

const hintStyle: React.CSSProperties = { color: "#FBBF24", fontSize: 12, marginBottom: 8 };

export function SigningMethodChoice({ agreement }: Props) {
  const [emailFormOpen, setEmailFormOpen] = useState(false);
  const [email, setEmail] = useState("");
  const chooseSigningMethod = useChooseSigningMethod(agreement.id);

  if (agreement.status !== "awaiting_signature") return null;

  if (agreement.signingMethod === "physical") {
    return (
      <div style={hintStyle}>
        Обрано: Фізичне підписання — надішліть підписаний договір на адресу
        постачальника
        {agreement.supplierLegalAddress ? `: ${agreement.supplierLegalAddress}` : ""}.
      </div>
    );
  }

  if (agreement.signingMethod === "vchasno") {
    return (
      <div style={hintStyle}>
        Обрано: Вчасно — документ надіслано на {agreement.signingEmail}.
      </div>
    );
  }

  function handlePhysical() {
    const address = agreement.supplierLegalAddress
      ? `\n\nАдреса постачальника: ${agreement.supplierLegalAddress}`
      : "";
    const confirmed = window.confirm(
      `Ви обираєте фізичне підписання. Роздрукуйте, підпишіть і надішліть договір поштою на адресу постачальника.${address}\n\nПідтвердити вибір?`
    );
    if (!confirmed) return;
    chooseSigningMethod.mutate(
      { method: "physical" },
      {
        onSuccess: () => toast.success("Обрано фізичне підписання"),
        onError: (err) => toast.error(err.message),
      }
    );
  }

  function handleVchasnoSubmit() {
    const trimmed = email.trim();
    if (!trimmed || !trimmed.includes("@")) {
      toast.error("Вкажіть коректний email");
      return;
    }
    chooseSigningMethod.mutate(
      { method: "vchasno", email: trimmed },
      {
        onSuccess: () => {
          toast.success("Договір надіслано у Вчасно");
          setEmailFormOpen(false);
        },
        onError: (err) => toast.error(err.message),
      }
    );
  }

  return (
    <div style={{ marginBottom: 8 }}>
      <div style={hintStyle}>
        Оберіть спосіб підписання договору — постачальник підтвердить
        підписання, після чого відкриються замовлення.
      </div>
      <div style={{ display: "flex", alignItems: "flex-start", gap: 8, flexWrap: "wrap" }}>
        <Btn
          size="sm"
          icon={<FileSignature size={13} />}
          disabled={chooseSigningMethod.isPending}
          onClick={handlePhysical}
        >
          Підписати фізично
        </Btn>

        {!emailFormOpen ? (
          <Btn
            size="sm"
            variant="ghost"
            icon={<Send size={13} />}
            disabled={chooseSigningMethod.isPending}
            onClick={() => setEmailFormOpen(true)}
          >
            Підписати через Вчасно
          </Btn>
        ) : (
          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="email для Вчасно"
              autoFocus
              style={{
                background: "#1F2937",
                border: "1px solid #374151",
                borderRadius: 6,
                color: "#E8EDF5",
                fontSize: 12,
                padding: "5px 8px",
                outline: "none",
                width: 200,
              }}
            />
            <Btn
              size="sm"
              disabled={chooseSigningMethod.isPending}
              onClick={handleVchasnoSubmit}
            >
              {chooseSigningMethod.isPending ? "Надсилання..." : "Надіслати"}
            </Btn>
            <Btn
              size="sm"
              variant="ghost"
              disabled={chooseSigningMethod.isPending}
              onClick={() => {
                setEmailFormOpen(false);
                setEmail("");
              }}
            >
              Скасувати
            </Btn>
          </div>
        )}
      </div>
    </div>
  );
}
