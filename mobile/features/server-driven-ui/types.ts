import type { ComponentType } from 'react';
import type { MobileBlockConfig } from '@/features/mobile-config/types';

export interface BlockComponentProps<TProps> {
  block: MobileBlockConfig<TProps>;
}

export type BlockComponent<TProps> = ComponentType<BlockComponentProps<TProps>>;

export interface RendererWarning {
  code: 'unknown_block' | 'invalid_block_props' | 'block_render_error';
  blockId: string;
  blockType: string;
  error?: Error;
}

export type RendererLogger = (warning: RendererWarning) => void;
