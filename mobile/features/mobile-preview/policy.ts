export function canEnableMobilePreview(isDevelopment: boolean, token: string): boolean {
  const length = token.trim().length;
  return isDevelopment && length >= 16 && length <= 2048;
}

export function previewRequestHeaders(token: string): Record<string, string> {
  return { 'X-Mobile-Preview-Token': token.trim() };
}
