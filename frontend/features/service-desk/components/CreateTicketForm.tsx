"use client";

import { useState } from "react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { useCreateTicket } from "../hooks/useTickets";
import { useLocations } from "@/features/locations/hooks/useLocations";
import type { TicketCategory, TicketPriority } from "../types";
import {
  TICKET_CATEGORIES,
  TICKET_PRIORITIES,
  getTicketCategoryLabel,
  getTicketPriorityLabel,
} from "../types";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { inputStyle, selectStyle, textareaStyle, labelStyle } from "@/components/ui/fieldStyles";

interface Props {
  onClose: () => void;
}

export function CreateTicketForm({ onClose }: Props) {
  const t = useTranslations("Dashboard.serviceDesk.createForm");
  const tCategories = useTranslations("Dashboard.serviceDesk.categories");
  const tPriorities = useTranslations("Dashboard.serviceDesk.priorities");
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [category, setCategory] = useState<TicketCategory>("general");
  const [priority, setPriority] = useState<TicketPriority>("medium");
  const [locationId, setLocationId] = useState("");

  const { data: locations } = useLocations();
  const createMutation = useCreateTicket();

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!title.trim() || !description.trim()) return;

    createMutation.mutate(
      {
        title: title.trim(),
        description: description.trim(),
        category,
        priority,
        locationId: locationId || undefined,
      },
      {
        onSuccess: () => {
          toast.success(t("toastCreated"));
          onClose();
        },
        onError: (err) => toast.error(err.message),
      }
    );
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={520}>
      <p style={{ color: "#4B5563", fontSize: 12, margin: "-14px 0 16px" }}>{t("subtitle")}</p>

        {/* Form */}
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          {/* Title */}
          <div>
            <label style={labelStyle}>
              {t("titleFieldLabel")} <span style={{ color: "#DC2626" }}>*</span>
            </label>
            <input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder={t("titlePlaceholder")}
              required
              style={inputStyle}
            />
          </div>

          {/* Description */}
          <div>
            <label style={labelStyle}>
              {t("descriptionFieldLabel")} <span style={{ color: "#DC2626" }}>*</span>
            </label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder={t("descriptionPlaceholder")}
              required
              rows={4}
              style={textareaStyle}
            />
          </div>

          {/* Category + Priority */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div>
              <label style={labelStyle}>{t("categoryLabel")}</label>
              <select
                value={category}
                onChange={(e) => setCategory(e.target.value as TicketCategory)}
                style={{ ...selectStyle, cursor: "pointer" }}
              >
                {TICKET_CATEGORIES.map((c) => (
                  <option key={c} value={c}>{getTicketCategoryLabel(tCategories, c)}</option>
                ))}
              </select>
            </div>

            <div>
              <label style={labelStyle}>{t("priorityLabel")}</label>
              <select
                value={priority}
                onChange={(e) => setPriority(e.target.value as TicketPriority)}
                style={{ ...selectStyle, cursor: "pointer" }}
              >
                {TICKET_PRIORITIES.map((p) => (
                  <option key={p} value={p}>{getTicketPriorityLabel(tPriorities, p)}</option>
                ))}
              </select>
            </div>
          </div>

          {/* Location */}
          <div>
            <label style={labelStyle}>{t("locationLabel")}</label>
            <select
              value={locationId}
              onChange={(e) => setLocationId(e.target.value)}
              style={{ ...selectStyle, cursor: "pointer" }}
            >
              <option value="">{t("noLocationOption")}</option>
              {locations?.filter((l) => l.isActive).map((loc) => (
                <option key={loc.id} value={loc.id}>{loc.name}</option>
              ))}
            </select>
          </div>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
            <Btn type="button" variant="ghost" onClick={onClose}>
              {t("cancelButton")}
            </Btn>
            <Btn type="submit" disabled={!title.trim() || !description.trim() || createMutation.isPending}>
              {createMutation.isPending ? t("creatingButton") : t("createButton")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}
