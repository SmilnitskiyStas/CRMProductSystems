let handler: (() => Promise<string | null>) | null = null;
export function registerMfaPrompt(next: () => Promise<string | null>): () => void {
  handler = next;
  return () => { if (handler === next) handler = null; };
}
export const requestMfaCode = (): Promise<string | null> => handler ? handler() : Promise.resolve(null);
