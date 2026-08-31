import { buildReceiptHtml } from '../receiptPrinting';

test('builds a printable receipt and escapes product data', () => {
  const html = buildReceiptHtml({
    transactionId: 'transaction-123',
    items: [{ barcode: '1', productName: '<Молоко & сир>', quantity: 2, unitPrice: 10, lineTotal: 20 }],
    subtotal: 20,
    paymentType: 'Cash',
    paymentAmount: 20,
    change: 0,
    fiscalStatus: 'fiscalized',
    fiscalNumber: 'FN-1',
  });

  expect(html).toContain('&lt;Молоко &amp; сир&gt;');
  expect(html).toContain('20.00 ₴');
  expect(html).not.toContain('<Молоко & сир>');
});
