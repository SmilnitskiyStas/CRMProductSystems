type Prompt = () => Promise<string | null>;
let prompt: Prompt | null = null;
export function registerMfaPrompt(handler: Prompt): () => void {
  prompt = handler;
  return () => { if (prompt === handler) prompt = null; };
}
export function requestMfaCode(): Promise<string | null> {
  return prompt ? prompt() : Promise.resolve(null);
}
