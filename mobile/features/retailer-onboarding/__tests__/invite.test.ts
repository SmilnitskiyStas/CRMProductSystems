import { parseRetailerInvite } from '../invite';

describe('retailer invite parser', () => {
  test.each([
    ['shelfguard://join/svizhyi-kut', 'custom-link'],
    ['https://app.shelfguard.ua/join/Svizhyi-Kut', 'universal-link'],
  ])('accepts trusted slug form %s', (value, source) => {
    expect(parseRetailerInvite(value)).toEqual({ slug: 'svizhyi-kut', source });
  });

  test.each([
    'https://evil.example/join/svizhyi-kut',
    'javascript://join/svizhyi-kut',
    'shelfguard://retailer/svizhyi-kut',
    'shelfguard://join/not_valid',
    'shelfguard://join/svizhyi-kut?redirect=evil',
    'SGRTL1.123e4567-e89b-42d3-a456-426614174000',
  ])('rejects obsolete, untrusted, or malformed input %s', (value) => {
    expect(parseRetailerInvite(value)).toBeNull();
  });
});
