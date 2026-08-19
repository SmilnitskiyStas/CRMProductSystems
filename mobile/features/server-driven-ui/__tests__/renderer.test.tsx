import { render } from '@testing-library/react-native';
import { Text } from 'react-native';
import type { MobileBlockConfig, MobilePageConfig } from '@/features/mobile-config/types';
import { createMockMobileConfig } from '@/features/mobile-config/mock';
import { BlockRenderer } from '../BlockRenderer';
import { PageBlockList } from '../PageRenderer';
import { ComponentRegistry } from '../registry';
import type { RendererLogger } from '../types';

interface LabelProps {
  label: string;
}

function isLabelProps(value: unknown): value is LabelProps {
  return typeof value === 'object' && value !== null && typeof (value as { label?: unknown }).label === 'string';
}

function LabelBlock({ block }: { block: MobileBlockConfig<LabelProps> }) {
  return <Text>{block.props.label}</Text>;
}

function createRegistry() {
  return new ComponentRegistry().register('label', LabelBlock, isLabelProps);
}

describe('server-driven UI renderer', () => {
  test('renders registered blocks in configured order without mutating the page', async () => {
    const page: MobilePageConfig = {
      blocks: [
        { id: 'second', type: 'label', order: 2, props: { label: 'Другий' } },
        { id: 'first', type: 'label', order: 1, props: { label: 'Перший' } },
      ],
    };
    const screen = await render(<PageBlockList page={page} registry={createRegistry()} />);
    expect(screen.getAllByText(/Перший|Другий/).map((node) => node.props.children)).toEqual([
      'Перший',
      'Другий',
    ]);
    expect(page.blocks[0].id).toBe('second');
  });

  test('ignores and reports unknown blocks without crashing the page', async () => {
    const logger = jest.fn<ReturnType<RendererLogger>, Parameters<RendererLogger>>();
    const screen = await render(
      <BlockRenderer
        block={{ id: 'unknown-1', type: 'futureBlock', props: {} }}
        registry={createRegistry()}
        logger={logger}
      />
    );
    expect(screen.toJSON()).toBeNull();
    expect(logger).toHaveBeenCalledWith({
      code: 'unknown_block',
      blockId: 'unknown-1',
      blockType: 'futureBlock',
    });
  });

  test('ignores a known block whose props fail its local validator', async () => {
    const logger = jest.fn<ReturnType<RendererLogger>, Parameters<RendererLogger>>();
    await render(
      <BlockRenderer
        block={{ id: 'label-1', type: 'label', props: { wrong: true } }}
        registry={createRegistry()}
        logger={logger}
      />
    );
    expect(logger).toHaveBeenCalledWith({
      code: 'invalid_block_props',
      blockId: 'label-1',
      blockType: 'label',
    });
  });

  test('rejects executable and insecure image URL schemes', async () => {
    const logger = jest.fn<ReturnType<RendererLogger>, Parameters<RendererLogger>>();
    await render(
      <BlockRenderer
        block={{ id: 'hero-unsafe', type: 'heroBanner', props: { title: 'Unsafe', imageUrl: 'javascript:alert(1)' } }}
        logger={logger}
      />
    );
    expect(logger).toHaveBeenCalledWith({
      code: 'invalid_block_props', blockId: 'hero-unsafe', blockType: 'heroBanner',
    });
  });

  test('isolates a component render failure from sibling blocks', async () => {
    const consoleError = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const logger = jest.fn<ReturnType<RendererLogger>, Parameters<RendererLogger>>();
    const registry = createRegistry().register(
      'crash',
      () => {
        throw new Error('broken block');
      },
      (_value): _value is Record<string, never> => true
    );
    const page: MobilePageConfig = {
      blocks: [
        { id: 'bad', type: 'crash', order: 1, props: {} },
        { id: 'good', type: 'label', order: 2, props: { label: 'Працює' } },
      ],
    };
    const screen = await render(<PageBlockList page={page} registry={registry} logger={logger} />);
    expect(screen.getByText('Працює')).toBeOnTheScreen();
    expect(logger).toHaveBeenCalledWith(
      expect.objectContaining({
        code: 'block_render_error',
        blockId: 'bad',
        blockType: 'crash',
      })
    );
    consoleError.mockRestore();
  });

  test('omits widgets whose required feature is disabled', async () => {
    const config = createMockMobileConfig('tenant-a');
    const page: MobilePageConfig = {
      blocks: [
        { id: 'catalog', type: 'label', feature: 'catalog', props: { label: 'Каталог' } },
        { id: 'coupon', type: 'label', feature: 'coupons', props: { label: 'Купон' } },
      ],
    };
    const screen = await render(
      <PageBlockList page={page} features={config.features} registry={createRegistry()} />
    );

    expect(screen.getByText('Каталог')).toBeOnTheScreen();
    expect(screen.queryByText('Купон')).toBeNull();
  });
});
