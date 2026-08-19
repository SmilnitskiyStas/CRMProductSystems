import { parseRetailerInvite } from '../invite';

const id = '123e4567-e89b-42d3-a456-426614174000';

describe('retailer invite parser', () => {
  test.each([
    [`SGRTL1.${id}`, 'payload'],
    [`shelfguard://retailer/${id}`, 'custom-link'],
    [`https://app.shelfguard.ua/retailer/${id}`, 'universal-link'],
  ])('accepts trusted versioned form %s', (value, source) => {
    expect(parseRetailerInvite(value)).toEqual({ tenantId: id, source });
  });

  test.each([
    `https://evil.example/retailer/${id}`,
    `javascript://retailer/${id}`,
    'SGRTL1.not-a-uuid',
    `https://app.shelfguard.ua/other/${id}`,
  ])('rejects untrusted or malformed input %s', (value) => {
    expect(parseRetailerInvite(value)).toBeNull();
  });
});
