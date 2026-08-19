import type { MobileBlockConfig } from '@/features/mobile-config/types';
import { BlockErrorBoundary } from './BlockErrorBoundary';
import { defaultRendererLogger } from './logger';
import { componentRegistry } from './coreRegistry';
import type { ComponentRegistry } from './registry';
import type { RendererLogger } from './types';

interface Props {
  block: MobileBlockConfig;
  registry?: ComponentRegistry;
  logger?: RendererLogger;
}

export function BlockRenderer({
  block,
  registry = componentRegistry,
  logger = defaultRendererLogger,
}: Props) {
  const definition = registry.get(block.type);
  if (!definition) {
    logger({ code: 'unknown_block', blockId: block.id, blockType: block.type });
    return null;
  }
  if (!definition.validateProps(block.props)) {
    logger({ code: 'invalid_block_props', blockId: block.id, blockType: block.type });
    return null;
  }

  return (
    <BlockErrorBoundary block={block} logger={logger}>
      {definition.render(block)}
    </BlockErrorBoundary>
  );
}
