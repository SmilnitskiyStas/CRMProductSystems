import { hasConfiguredPromotionsContent } from '../pageSelection';

describe('configured page selection', () => {
  it('keeps the API-backed promotions screen for an empty builder page', () => {
    expect(hasConfiguredPromotionsContent({ blocks: [] })).toBe(false);
  });

  it.each(['promotionGrid', 'promotionCarousel'])('uses the configured promotions page when it has %s', (type) => {
    expect(hasConfiguredPromotionsContent({
      blocks: [{ id: type, type, props: {} }],
    })).toBe(true);
  });

  it('does not mistake decorative blocks for promotions content', () => {
    expect(hasConfiguredPromotionsContent({
      blocks: [{ id: 'hero', type: 'heroBanner', props: { title: 'Вітаємо' } }],
    })).toBe(false);
  });
});
