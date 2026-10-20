// Generates demo barcode PNGs for the webcam scanner demo: node scripts/gen-barcodes.mjs
// Items come from the dev demo catalog (tenant "Svizhyi Kut"). Run from /cashier.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import bwipjs from 'bwip-js/node';

const outDir = join(import.meta.dirname, '..', 'dev-assets', 'barcodes');
mkdirSync(outDir, { recursive: true });

const ean13Check = (p12) => {
  const s = [...p12].reduce((a, d, i) => a + Number(d) * (i % 2 === 0 ? 1 : 3), 0);
  return String((10 - (s % 10)) % 10);
};

// [file name (ASCII), EAN-13 barcode]
const items = [
  ['01_Bread-Kyivkhlib-Stolychnyi-rye-36.11', '4823117506458'], // sold successfully in e2e runs
  ['02_Baton-Kyivkhlib-Pshenychnyi-24.12', '4823117506397'],
  ['03_Bread-Kyivkhlib-Pshenychnyi-27.25', '4823117506427'],
  ['04_Bag-small-5.00', '2900000100015'],
  ['99_UNKNOWN-barcode-not-in-catalog', '482000099999' + ean13Check('482000099999')],
];

for (const [name, code] of items) {
  const png = await bwipjs.toBuffer({
    bcid: 'ean13',
    text: code.slice(0, 12),
    scale: 5,
    height: 22,
    includetext: true,
    textxalign: 'center',
    paddingwidth: 24,
    paddingheight: 16,
    backgroundcolor: 'ffffff',
  });
  writeFileSync(join(outDir, `${name}__${code}.png`), png);
  console.log(`${name}__${code}.png`);
}
