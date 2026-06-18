"use client";

import { useState } from "react";
import { X, MapPin, User, Calendar, MessageSquare, Lock } from "lucide-react";
import { toast } from "sonner";
import { useTicket, useUpdateTicket, useAddComment } from "../hooks/useTickets";
import { useUsers } from "@/features/users/hooks/useUsers";
import type { TicketStatus, TicketCommentDto } from "../types";
import {
  TICKET_STATUS_LABELS,
  TICKET_PRIORITY_LABELS,
  TICKET_CATEGORY_LABELS,
} from "../types";
import { TicketStatusBadge } from "./TicketStatusBadge";
import { PriorityBadge } from "./PriorityBadge";
import { AT_LEAST_STORE_MANAGER } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";

interface Props {
  ticketId: string;
  userRole: AppRole | "";
  onClose: () => void;
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function CommentItem({ comment, isManager }: { comment: TicketCommentDto; isManager: boolean }) {
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
            Внутрішня
          </span>
        )}
        <span style={{ color: "#4B5563", fontSize: 11, marginLeft: "auto" }}>
          {formatDateTime(comment.createdAt)}
        </span>
      </div>
      <div style={{ color: "#9CA3AF", fontSize: 13, lineHeight: 1.5, whiteSpace: "pre-wrap" }}>
        {comment.body}
      </div>
    </div>
  );
}

export function TicketDetail({ ticketId, userRole, onClose }: Props) {
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
        onSuccess: () => toast.success("Статус оновлено"),
        onError: (err) => toast.error(err.message),
      }
    );
  }

  function handleAssignChange(assignedTo: string) {
    if (!ticket) return;
    updateMutation.mutate(
      { id: ticketId, data: { assignedTo: assignedTo || undefined } },
      {
        onSuccess: () => toast.success("Відповідального призначено"),
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
          toast.success("Коментар додано");
          setCommentBody("");
          setIsInternal(false);
        },
        onError: (err) => toast.error(err.message),
      }
    );
  }

  const selectStyle = {
    background: "#111827",
    border: "1px solid #1F2937",
    borderRadius: 6,
    color: "#9CA3AF",
    fontSize: 12,
    padding: "6px 10px",
    cursor: "pointer",
    outline: "none",
    width: "100%",
  };

  const labelStyle = {
    color: "#4B5563",
    fontSize: 11,
    fontWeight: 600,
    textTransform: "uppercase" as const,
    letterSpacing: "0.04em",
    marginBottom: 4,
    display: "block",
  };

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.5)",
          zIndex: 40,
        }}
      />

      {/* Sheet */}
      <div
        style={{
          position: "fixed",
          top: 0,
          right: 0,
          bottom: 0,
          width: "min(600px, 100vw)",
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 50,
          display: "flex",
          flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            padding: "16px 20px",
            borderBottom: "1px solid #1F2937",
            display: "flex",
            alignItems: "flex-start",
            gap: 12,
          }}
        >
          <div style={{ flex: 1 }}>
            {!isLoading && ticket && (
              <>
                <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 4 }}>
                  #{ticket.number}
                </div>
                <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, lineHeight: 1.4 }}>
                  {ticket.title}
                </div>
              </>
            )}
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "none",
              color: "#4B5563",
              cursor: "pointer",
              padding: 4,
              flexShrink: 0,
            }}
          >
            <X size={18} />
          </button>
        </div>

        {isLoading && (
          <div style={{ padding: 24, color: "#4B5563", fontSize: 13 }}>
            Завантаження...
          </div>
        )}

        {ticket && (
          <div style={{ padding: "20px", display: "flex", flexDirection: "column", gap: 20 }}>
            {/* Badges row */}
            <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
              <span
                style={{
                  background: "#1F2937",
                  color: "#9CA3AF",
                  borderRadius: 4,
                  padding: "2px 8px",
                  fontSize: 12,
                }}
              >
                {TICKET_CATEGORY_LABELS[ticket.category]}
              </span>
              <PriorityBadge priority={ticket.priority} />
              <TicketStatusBadge status={ticket.status} />
            </div>

            {/* Description */}
            <div>
              <span style={labelStyle}>Опис</span>
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
                  Автор
                </span>
                <div style={{ color: "#CBD5E1", fontSize: 13 }}>{ticket.createdByName}</div>
              </div>

              {ticket.locationName && (
                <div>
                  <span style={labelStyle}>
                    <MapPin size={10} style={{ display: "inline", marginRight: 4 }} />
                    Локація
                  </span>
                  <div style={{ color: "#CBD5E1", fontSize: 13 }}>{ticket.locationName}</div>
                </div>
              )}

              <div>
                <span style={labelStyle}>
                  <Calendar size={10} style={{ display: "inline", marginRight: 4 }} />
                  Створено
                </span>
                <div style={{ color: "#CBD5E1", fontSize: 13 }}>{formatDateTime(ticket.createdAt)}</div>
              </div>

              {ticket.resolvedAt && (
                <div>
                  <span style={labelStyle}>Вирішено</span>
                  <div style={{ color: "#86EFAC", fontSize: 13 }}>{formatDateTime(ticket.resolvedAt)}</div>
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
                  Управління тікетом
                </div>

                <div>
                  <span style={labelStyle}>Статус</span>
                  <select
                    value={ticket.status}
                    onChange={(e) => handleStatusChange(e.target.value as TicketStatus)}
                    style={selectStyle}
                    disabled={updateMutation.isPending}
                  >
                    {(Object.keys(TICKET_STATUS_LABELS) as TicketStatus[]).map((s) => (
                      <option key={s} value={s}>{TICKET_STATUS_LABELS[s]}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <span style={labelStyle}>Відповідальний</span>
                  <select
                    value={ticket.assignedTo ?? ""}
                    onChange={(e) => handleAssignChange(e.target.value)}
                    style={selectStyle}
                    disabled={updateMutation.isPending}
                  >
                    <option value="">— Не призначено —</option>
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
                Коментарі ({ticket.comments.length})
              </div>

              {ticket.comments.length === 0 && (
                <div style={{ color: "#4B5563", fontSize: 13, marginBottom: 12 }}>
                  Поки немає коментарів
                </div>
              )}

              <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 16 }}>
                {ticket.comments.map((comment) => (
                  <CommentItem key={comment.id} comment={comment} isManager={isManager} />
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
                  placeholder="Напишіть коментар..."
                  rows={3}
                  style={{
                    background: "#0D1117",
                    border: "1px solid #1F2937",
                    borderRadius: 6,
                    color: "#E8EDF5",
                    fontSize: 13,
                    padding: "8px 10px",
                    outline: "none",
                    resize: "vertical",
                    width: "100%",
                    boxSizing: "border-box",
                    fontFamily: "inherit",
                  }}
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
                      Внутрішня нотатка
                    </label>
                  )}

                  <button
                    onClick={handleAddComment}
                    disabled={!commentBody.trim() || addCommentMutation.isPending}
                    style={{
                      background: commentBody.trim() ? "#1D3461" : "#111827",
                      border: `1px solid ${commentBody.trim() ? "#3B82F6" : "#1F2937"}`,
                      borderRadius: 6,
                      color: commentBody.trim() ? "#93C5FD" : "#374151",
                      fontSize: 12,
                      fontWeight: 600,
                      padding: "6px 14px",
                      cursor: commentBody.trim() ? "pointer" : "not-allowed",
                      marginLeft: "auto",
                    }}
                  >
                    {addCommentMutation.isPending ? "Надсилання..." : "Надіслати"}
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    </>
  );
}
