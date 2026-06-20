import { useMutation } from '@tanstack/react-query';
import { sendAssistantMessage } from '../api';
import type { AiAssistantRequest } from '../types';

export function useAiAssistant() {
  return useMutation({
    mutationFn: (req: AiAssistantRequest) => sendAssistantMessage(req),
  });
}
