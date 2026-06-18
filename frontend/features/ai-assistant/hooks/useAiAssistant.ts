import { useMutation } from "@tanstack/react-query";
import { aiAssistantApi } from "../api/aiAssistant";
import type { AiAssistantRequest, AiAssistantResponse } from "../types";

export function useAiAssistant() {
  return useMutation<AiAssistantResponse, Error, AiAssistantRequest>({
    mutationFn: (request) => aiAssistantApi.ask(request),
  });
}
