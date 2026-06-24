"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { chatApi, providerChatApi } from "../api/chat-api";
import type { CreateChatSessionRequest, SendChatMessageRequest, SubmitRatingRequest } from "../types";

// ── Query keys ────────────────────────────────────────────────────────────────

export const CHAT_SESSIONS_KEY = ["chat", "sessions"] as const;
const chatMessagesKey = (sessionId: string) => ["chat", "messages", sessionId] as const;

const PROVIDER_CHAT_SESSIONS_KEY = ["provider", "chat", "sessions"] as const;
const providerChatMessagesKey = (sessionId: string) =>
  ["provider", "chat", "messages", sessionId] as const;

// ── Client hooks ──────────────────────────────────────────────────────────────

export function useMyChats() {
  return useQuery({
    queryKey: CHAT_SESSIONS_KEY,
    queryFn: chatApi.getSessions,
    refetchInterval: 3000,
  });
}

export function useChatMessages(sessionId: string | null) {
  return useQuery({
    queryKey: chatMessagesKey(sessionId ?? ""),
    queryFn: () => chatApi.getMessages(sessionId!),
    enabled: Boolean(sessionId),
    refetchInterval: 3000,
  });
}

export function useCreateChat() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateChatSessionRequest) => chatApi.createSession(req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: CHAT_SESSIONS_KEY });
    },
  });
}

export function useSendMessage(sessionId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: SendChatMessageRequest) => chatApi.sendMessage(sessionId!, req),
    onSuccess: () => {
      if (sessionId) {
        qc.invalidateQueries({ queryKey: chatMessagesKey(sessionId) });
        qc.invalidateQueries({ queryKey: CHAT_SESSIONS_KEY });
      }
    },
  });
}

export function useCloseChat() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (sessionId: string) => chatApi.closeSession(sessionId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: CHAT_SESSIONS_KEY });
    },
  });
}

export function useSubmitRating() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ sessionId, req }: { sessionId: string; req: SubmitRatingRequest }) =>
      chatApi.submitRating(sessionId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: CHAT_SESSIONS_KEY });
    },
  });
}

// ── Provider hooks ────────────────────────────────────────────────────────────

export function useProviderChats() {
  return useQuery({
    queryKey: PROVIDER_CHAT_SESSIONS_KEY,
    queryFn: providerChatApi.getAllSessions,
    refetchInterval: 3000,
  });
}

export function useProviderChatMessages(sessionId: string | null) {
  return useQuery({
    queryKey: providerChatMessagesKey(sessionId ?? ""),
    queryFn: () => providerChatApi.getMessages(sessionId!),
    enabled: Boolean(sessionId),
    refetchInterval: 3000,
  });
}

export function useProviderSendMessage(sessionId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: SendChatMessageRequest) =>
      providerChatApi.sendMessage(sessionId!, req),
    onSuccess: () => {
      if (sessionId) {
        qc.invalidateQueries({ queryKey: providerChatMessagesKey(sessionId) });
        qc.invalidateQueries({ queryKey: PROVIDER_CHAT_SESSIONS_KEY });
      }
    },
  });
}

export function useProviderCloseChat() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (sessionId: string) => providerChatApi.closeSession(sessionId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PROVIDER_CHAT_SESSIONS_KEY });
    },
  });
}
