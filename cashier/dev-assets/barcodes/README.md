# Demo barcodes for the webcam scanner

EAN-13 PNGs of items from the dev demo catalog (regenerate with `node scripts/gen-barcodes.mjs`).
Filename = `<n>_<product>-<price>__<barcode>.png`.

Demo: open the cashier app, sign in, open a shift, press **F3 / "Камера"**, then show a PNG
on a phone screen (max brightness, no glare) to the laptop camera at ~15-25 cm inside the frame,
or print it. `01` is verified sellable; `02`-`04` come from the same catalog (stock/expiry on the
server was not checked, an expired item is rejected at sync). `99_UNKNOWN` is not in the catalog:
expect the red flash, low tone and "не знайдено" message.
