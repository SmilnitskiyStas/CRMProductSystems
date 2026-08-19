import { createElement, type ReactNode } from 'react';
import type { MobileBlockConfig } from '@/features/mobile-config/types';
import type { BlockComponent } from './types';

interface RegisteredBlock {
  validateProps: (value: unknown) => boolean;
  render: (block: MobileBlockConfig) => ReactNode;
}

export class ComponentRegistry {
  private readonly definitions = new Map<string, RegisteredBlock>();

  register<TProps>(
    type: string,
    component: BlockComponent<TProps>,
    validateProps: (value: unknown) => value is TProps
  ): this {
    this.definitions.set(type, {
      validateProps,
      render: (block) =>
        createElement(component, {
          block: { ...block, props: block.props as TProps },
        }),
    });
    return this;
  }

  get(type: string): RegisteredBlock | undefined {
    return this.definitions.get(type);
  }

  has(type: string): boolean {
    return this.definitions.has(type);
  }
}
