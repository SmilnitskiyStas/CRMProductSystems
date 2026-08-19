import type { RendererLogger } from './types';

export const defaultRendererLogger: RendererLogger = (warning) => {
  if (__DEV__) console.warn(`[mobile-config:${warning.code}]`, {
    blockId: warning.blockId,
    blockType: warning.blockType,
    error: warning.error?.message,
  });
};
