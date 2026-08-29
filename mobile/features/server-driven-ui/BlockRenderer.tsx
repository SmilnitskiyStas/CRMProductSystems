import { useEffect, useRef, useState, type ReactNode } from 'react';
import { AccessibilityInfo, Animated, Easing } from 'react-native';
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

type VisualEffect = { border?: 'none' | 'solid' | 'gradient'; speed?: 'slow' | 'normal' | 'fast'; color?: string; secondaryColor?: string };

function VisualEffectWrapper({ effect, children }: { effect: VisualEffect; children: ReactNode }) {
  const progress = useRef(new Animated.Value(0)).current;
  const [reduceMotion, setReduceMotion] = useState(false);
  useEffect(() => { void AccessibilityInfo.isReduceMotionEnabled().then(setReduceMotion); }, []);
  useEffect(() => {
    if (effect.border === 'none' || reduceMotion) { progress.stopAnimation(); progress.setValue(0); return; }
    const duration = effect.speed === 'slow' ? 2600 : effect.speed === 'fast' ? 800 : 1500;
    const animation = Animated.loop(Animated.timing(progress, { toValue: 1, duration, easing: Easing.linear, useNativeDriver: false }));
    animation.start(); return () => animation.stop();
  }, [effect.border, effect.speed, progress, reduceMotion]);
  if (!effect.border || effect.border === 'none') return children;
  const primary = effect.color ?? '#3B82F6'; const secondary = effect.secondaryColor ?? '#A855F7';
  const borderColor = effect.border === 'gradient' ? progress.interpolate({ inputRange: [0, .5, 1], outputRange: [primary, secondary, primary] }) : primary;
  const opacity = effect.border === 'solid' && !reduceMotion ? progress.interpolate({ inputRange: [0, .5, 1], outputRange: [.45, 1, .45] }) : 1;
  return <Animated.View style={{ borderWidth: 2, borderColor, opacity, borderRadius: 20, padding: 2 }}>{children}</Animated.View>;
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
  const props = block.props && typeof block.props === 'object' ? block.props as Record<string, unknown> : {};
  const effect = props._visualEffect && typeof props._visualEffect === 'object' ? props._visualEffect as VisualEffect : { border: 'none' as const };
  const renderProps = { ...props };
  delete renderProps._visualEffect;
  const renderBlock = { ...block, props: renderProps };
  if (!definition.validateProps(renderProps)) {
    logger({ code: 'invalid_block_props', blockId: block.id, blockType: block.type });
    return null;
  }

  return (
    <BlockErrorBoundary block={block} logger={logger}>
      <VisualEffectWrapper effect={effect}>{definition.render(renderBlock)}</VisualEffectWrapper>
    </BlockErrorBoundary>
  );
}
