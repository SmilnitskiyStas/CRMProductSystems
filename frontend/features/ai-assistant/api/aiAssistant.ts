import { api } from "@/lib/api";
import type { AiAssistantRequest, AiAssistantResponse } from "../types";

export const aiAssistantApi = {
  ask: (request: AiAssistantRequest) =>
    api.post<AiAssistantResponse>("/api/ai/assistant", request),
};
