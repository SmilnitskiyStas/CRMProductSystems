export interface AiAssistantRequest {
  message: string;
}

export interface AiAssistantContextSummary {
  criticalStockBatchesCount: number;
  pendingOrdersCount: number;
  salesDaysCount: number;
  activeSuppliersCount: number;
}

export interface AiAssistantResponse {
  reply: string;
  context: AiAssistantContextSummary;
  aiModel: string;
  tokensUsed: number;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  text: string;
  context?: AiAssistantContextSummary;
}
