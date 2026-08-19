import { Component, type ErrorInfo, type ReactNode } from 'react';
import type { MobileBlockConfig } from '@/features/mobile-config/types';
import type { RendererLogger } from './types';

interface Props {
  block: MobileBlockConfig;
  logger: RendererLogger;
  children: ReactNode;
}

interface State {
  failed: boolean;
}

export class BlockErrorBoundary extends Component<Props, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  componentDidCatch(error: Error, _info: ErrorInfo) {
    this.props.logger({
      code: 'block_render_error',
      blockId: this.props.block.id,
      blockType: this.props.block.type,
      error,
    });
  }

  render() {
    return this.state.failed ? null : this.props.children;
  }
}
