"use client";

import { useState } from "react";
import { MapPin, User, Calendar, MessageSquare, Lock } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import { useTicket, useUpdateTicket, useAddComment } from "../hooks/useTickets";
import { useUsers } from "@/features/users/hooks/useUsers";
import type { TicketStatus, TicketCommentDto } from "../types";
import {
  TICKET_STATUSES,
  getTicketStatusLabel,
  getTicketCategoryLabel,
} from "../types";
import { TicketStatusBadge } from "./TicketStatusBadge";
import { PriorityBadge } from "./PriorityBadge";
import { AT_LEAST_STORE_MANAGER } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";
import { Btn } from "@/components/ui/Btn";
import { DetailDrawer } from "@/components/ui/DetailDrawer";
import { StatusPill } from "@/components/ui/StatusPill";
import { filterInputStyle, labelStyle, textareaStyle } from "@/components/ui/fieldStyles";

interface Props {
  ticketId: string;
  userRole: AppRole | "";
  onClose: () => void;
}

function formatDateTime(iso: string, locale: string): string {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function CommentItem({ comment, isManager, locale }: { comment: TicketCommentDto; isManager: boolean; locale: string }) {
  const t = useTranslations("Dashboard.serviceDesk.ticketDetail");
  if (comment.isInternal && !isManager) return null;

  return (
    <div
      style={{
        background: comment.isInternal ? "#1A0F00" : "#111827",
        border: `1px solid ${comment.isInternal ? "#92400E" : "#1F2937"}`,
        borderRadius: 8,
        padding: "10px 14px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
        <span style={{ color: "#CBD5E1", fontSize: 13, fontWeight: 600 }}>
          {comment.authorName}
        </span>
        {comment.isInternal && (
          <span
            style={{
              display: "flex",
              alignItems: "center",
              gap: 3,
              background: "#451A03",
              color: "#FDBA74",
              border: "1px solid #92400E",
              borderRadius: 4,
              padding: "1px 6px",
              fontSize: 10,
              fontWeight: 600,
            }}
          >
            <Lock size={10} />
            {t("internalBadge")}
          </span>
        )}
        <span style={{ color: "#4B5563", fontSize: 11, marginLeft: "auto" }}>
          {formatDateTime(comment.createdAt, locale)}
        </span>
      </div>
      <div style={{ color: "#9CA3AF", fontSize: 13, lineHeight: 1.5, whiteSpace: "pre-wrap" }}>
        {comment.body}
      </div>
    </div>
  );
}

export function TicketDetail({ ticketId, userRole, onClose }: Props) {
  const t = useTranslations("Dashboard.serviceDesk.ticketDetail");
  const tStatuses = useTranslations("Dashboard.serviceDesk.statuses");
  const tCategories = useTranslations("Dashboard.serviceDesk.categories");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const isManager = AT_LEAST_STORE_MANAGER.has(userRole as AppRole);

  const { data: ticket, isLoading } = useTicket(ticketId);
  const { data: users } = useUsers();
  const updateMutation = useUpdateTicket();
  const addCommentMutation = useAddComment();

  const [commentBody, setCommentBody] = useState("");
  const [isInternal, setIsInternal] = useState(false);

  function handleStatusChange(newStatus: TicketStatus) {
    if (!ticket) return;
    updateMutation.mutate(
      { id: ticketId, data: { status: newStatus } },
      {
        onSuccess: () => toast.success(t("toastStatusUpdated")),
        onError: (err) => toast.error(err.message),
      }
    );
  }

  function handleAssignChange(assignedTo: string) {
    if (!ticket) return;
    updateMutation.mutate(
      { id: ticketId, data: { assignedTo: assignedTo || undefined } },
      {
        onSuccess: () => toast.success(t("toastAssigneeUpdated")),
        onError: (err) => toast.error(err.message),
      }
    );
  }

  function handleAddComment() {
    if (!commentBody.trim()) return;
    addCommentMutation.mutate(
      { id: ticketId, data: { body: commentBody.trim(), isInternal } },
      {
        onSuccess: () => {
          toast.success(t("toastCommentAdded"));
          setCommentBody("");
          setIsInternal(false);
        },
        onError: (err) => toast.error(err.message),
      }
    );
  }

  return (
    <DetailDrawer
      isOpen
      onClose={onClose}
      title={!isLoading && ticket ? ticket.title : ""}
      subtitle={!isLoading && ticket ? `#${ticket.number}` : undefined}
      width={600}
    >
        {isLoading && (
          <div style={{ color: "#4B5563", fontSize: 13 }}>
            {t("loading")}
          </div>
        )}

        {ticket && (
          <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
            {/* Badges row */}
            <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
              <StatusPill bg="#1F2937" text="#9CA3AF">
                {getTicketCategoryLabel(tCategories, ticket.category)}
              </StatusPill>
              <PriorityBadge priority={ticket.priority} />
              <TicketStatusBadge status={ticket.status} />
            </div>

            {/* Description */}
            <div>
              <span style={labelStyle}>{t("descriptionLabel")}</span>
              <div
                style={{
                  color: "#9CA3AF",
                  fontSize: 13,
                  lineHeight: 1.6,
                  whiteSpace: "pre-wrap",
                  background: "#111827",
                  border: "1px solid #1F2937",
                  borderRadius: 8,
                  padding: "12px 14px",
                }}
              >
                {ticket.description}
              </div>
            </div>

            {/* Meta info grid */}
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1fr 1fr",
                gap: 12,
              }}
            >
              <div>
                <span style={labelStyle}>
                  <User size={10} style={{ display: "inline", marginRight: 4 }} />
                  {t("authorLabel")}
                </span>
                <div style={{ color: "#CBD5E1", fontSize: 13 }}>{ticket.createdByName}</div>
              </div>

              {ticket.locationName && (
                <div>
                  <span style={labelStyle}>
                    <MapPin size={10} style={{ display: "inline", marginRight: 4 }} />
                    {t("locationLabel")}
                  </span>
                  <div style={{ color: "#CBD5E1", fontSize: 13 }}>{ticket.locationName}</div>
                </div>
              )}

              <div>
                <span style={labelStyle}>
                  <Calendar size={10} style={{ display: "inline", marginRight: 4 }} />
                  {t("createdLabel")}
                </span>
                <div style={{ color: "#CBD5E1", fontSize: 13 }}>{formatDateTime(ticket.createdAt, intlLocale)}</div>
              </div>

              {ticket.resolvedAt && (
                <div>
                  <span style={labelStyle}>{t("resolvedLabel")}</span>
                  <div style={{ color: "#86EFAC", fontSize: 13 }}>{formatDateTime(ticket.resolvedAt, intlLocale)}</div>
                </div>
              )}
            </div>

            {/* Manager controls */}
            {isManager && (
              <div
                style={{
                  background: "#111827",
                  border: "1px solid #1F2937",
                  borderRadius: 10,
                  padding: "16px",
                  display: "flex",
                  flexDirection: "column",
                  gap: 12,
                }}
              >
                <div style={{ color: "#CBD5E1", fontSize: 13, fontWeight: 600 }}>
                  {t("manageTitle")}
                </div>

                <div>
                  <span style={labelStyle}>{t("statusLabel")}</span>
                  <select
                    value={ticket.status}
                    onChange={(e) => handleStatusChange(e.target.value as TicketStatus)}
                    style={{ ...filterInputStyle, width: "100%" }}
                    disabled={updateMutation.isPending}
                  >
                    {TICKET_STATUSES.map((s) => (
                      <option key={s} value={s}>{getTicketStatusLabel(tStatuses, s)}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <span style={labelStyle}>{t("assigneeLabel")}</span>
                  <select
                    value={ticket.assignedTo ?? ""}
                    onChange={(e) => handleAssignChange(e.target.value)}
                    style={{ ...filterInputStyle, width: "100%" }}
                    disabled={updateMutation.isPending}
                  >
                    <option value="">{t("unassignedOption")}</option>
                    {users?.filter((u) => u.isActive).map((u) => (
                      <option key={u.id} value={u.id}>{u.fullName}</option>
                    ))}
                  </select>
                </div>
              </div>
            )}

            {/* Comments section */}
            <div>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  marginBottom: 12,
                  color: "#CBD5E1",
                  fontSize: 14,
                  fontWeight: 600,
                }}
              >
                <MessageSquare size={15} />
                {t("commentsTitle", { count: ticket.comments.length })}
              </div>

              {ticket.comments.length === 0 && (
                <div style={{ color: "#4B5563", fontSize: 13, marginBottom: 12 }}>
                  {t("noComments")}
                </div>
              )}

              <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 16 }}>
                {ticket.comments.map((comment) => (
                  <CommentItem key={comment.id} comment={comment} isManager={isManager} locale={intlLocale} />
                ))}
              </div>

              {/* Add comment form */}
              <div
                style={{
                  background: "#111827",
                  border: "1px solid #1F2937",
                  borderRadius: 8,
                  padding: 12,
                  display: "flex",
                  flexDirection: "column",
                  gap: 8,
                }}
              >
                <textarea
                  value={commentBody}
                  onChange={(e) => setCommentBody(e.target.value)}
                  placeholder={t("commentPlaceholder")}
                  rows={3}
                  style={textareaStyle}
                />

                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                  {isManager && (
                    <label
                      style={{
                        display: "flex",
                        alignItems: "center",
                        gap: 6,
                        color: "#9CA3AF",
                        fontSize: 12,
                        cursor: "pointer",
                      }}
                    >
                      <input
                        type="checkbox"
                        checked={isInternal}
                        onChange={(e) => setIsInternal(e.target.checked)}
                        style={{ accentColor: "#F97316" }}
                      />
                      {t("internalNoteLabel")}
                    </label>
                  )}

                  <Btn
                    size="sm"
                    onClick={handleAddComment}
                    disabled={!commentBody.trim() || addCommentMutation.isPending}
                    style={{ marginLeft: "auto" }}
                  >
                    {addCommentMutation.isPending ? t("sendingButton") : t("sendButton")}
                  </Btn>
                </div>
              </div>
            </div>
          </div>
        )}
    </DetailDrawer>
  );
}
