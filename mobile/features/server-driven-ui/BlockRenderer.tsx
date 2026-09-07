import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { AccessibilityInfo, Animated, Easing, type LayoutChangeEvent, View } from 'react-native';
import Svg, { Defs, LinearGradient, Rect, Stop } from 'react-native-svg';
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

type VisualEffect = { border?: 'none' | 'solid' | 'gradient'; speed?: 'slow' | 'normal' | 'fast'; trailLength?: number; neonGlow?: boolean; neonGlowMode?: 'static' | 'moving'; neonGlowColor?: string; color?: string; secondaryColor?: string };

const AnimatedRect = Animated.createAnimatedComponent(Rect);
const BORDER_WIDTH = 2;
const CORNER_RADIUS = 20;

function roundedRectanglePerimeter(width: number, height: number, radius: number) {
  return 2 * (width + height) - 8 * radius + 2 * Math.PI * radius;
}

function VisualEffectWrapper({ effect, children }: { effect: VisualEffect; children: ReactNode }) {
  const [reduceMotion, setReduceMotion] = useState(false);
  const [size, setSize] = useState({ width: 0, height: 0 });
  const dashOffset = useRef(new Animated.Value(0)).current;
  const gradientId = useId().replace(/:/g, '');
  useEffect(() => { void AccessibilityInfo.isReduceMotionEnabled().then(setReduceMotion); }, []);

  const rectWidth = Math.max(0, size.width - BORDER_WIDTH * 2);
  const rectHeight = Math.max(0, size.height - BORDER_WIDTH * 2);
  const radius = Math.min(CORNER_RADIUS, rectWidth / 2, rectHeight / 2);
  const perimeter = roundedRectanglePerimeter(rectWidth, rectHeight, radius);
  const trailRatio = typeof effect.trailLength === 'number'
    ? Math.min(0.7, Math.max(0.08, effect.trailLength / 100))
    : 0.14;
  const dashLength = Math.min(perimeter * 0.8, Math.max(28, perimeter * trailRatio));
  const hasBorder = effect.border === 'solid' || effect.border === 'gradient';
  const movingNeon = effect.neonGlow === true && effect.neonGlowMode === 'moving' && !reduceMotion;
  const hasContour = hasBorder || movingNeon;

  useEffect(() => {
    dashOffset.stopAnimation();
    dashOffset.setValue(0);
    if (!hasContour || reduceMotion || perimeter <= 0) return;
    const duration = effect.speed === 'slow' ? 5200 : effect.speed === 'fast' ? 1600 : 3000;
    const animation = Animated.loop(
      Animated.timing(dashOffset, { toValue: -perimeter, duration, easing: Easing.linear, useNativeDriver: false })
    );
    animation.start();
    return () => animation.stop();
  }, [dashOffset, effect.speed, hasContour, perimeter, reduceMotion]);

  if (!hasBorder && !effect.neonGlow) return children;
  const primary = effect.color ?? '#3B82F6';
  const secondary = effect.secondaryColor ?? '#A855F7';
  const neonGlowColor = effect.neonGlowColor ?? primary;
  const stroke = effect.border === 'gradient' ? `url(#${gradientId})` : primary;
  const dashArray = `${dashLength} ${Math.max(1, perimeter - dashLength)}`;

  function handleLayout(event: LayoutChangeEvent) {
    const { width, height } = event.nativeEvent.layout;
    setSize((current) => current.width === width && current.height === height ? current : { width, height });
  }

  return (
    <View onLayout={handleLayout} style={{ position: 'relative', padding: hasContour ? BORDER_WIDTH : 0, shadowColor: effect.neonGlow && !movingNeon ? neonGlowColor : undefined, shadowOpacity: effect.neonGlow && !movingNeon ? 0.9 : 0, shadowRadius: effect.neonGlow && !movingNeon ? 10 : 0, shadowOffset: { width: 0, height: 0 }, elevation: effect.neonGlow && !movingNeon ? 8 : 0 }}>
      {hasContour && rectWidth > 0 && rectHeight > 0 ? (
        <Svg
          testID="visual-effect-border"
          pointerEvents="none"
          width={size.width}
          height={size.height}
          style={{ position: 'absolute', left: 0, top: 0 }}
        >
          {effect.border === 'gradient' ? (
            <Defs>
              <LinearGradient id={gradientId} x1="0" y1="0" x2="1" y2="1">
                <Stop offset="0" stopColor={primary} />
                <Stop offset="0.5" stopColor={secondary} />
                <Stop offset="1" stopColor={primary} />
              </LinearGradient>
            </Defs>
          ) : null}
          {movingNeon && <><AnimatedRect x={BORDER_WIDTH / 2} y={BORDER_WIDTH / 2} width={rectWidth} height={rectHeight} rx={radius} fill="none" stroke={neonGlowColor} strokeWidth={10} strokeOpacity={0.14} strokeLinecap="round" strokeDasharray={dashArray} strokeDashoffset={dashOffset} /><AnimatedRect x={BORDER_WIDTH / 2} y={BORDER_WIDTH / 2} width={rectWidth} height={rectHeight} rx={radius} fill="none" stroke={neonGlowColor} strokeWidth={6} strokeOpacity={0.38} strokeLinecap="round" strokeDasharray={dashArray} strokeDashoffset={dashOffset} /></>}
          {hasBorder && <AnimatedRect x={BORDER_WIDTH / 2} y={BORDER_WIDTH / 2} width={rectWidth} height={rectHeight} rx={radius} fill="none" stroke={stroke} strokeWidth={BORDER_WIDTH} strokeLinecap="round" strokeDasharray={reduceMotion ? undefined : dashArray} strokeDashoffset={reduceMotion ? 0 : dashOffset} />}
        </Svg>
      ) : null}
      {children}
    </View>
  );
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
