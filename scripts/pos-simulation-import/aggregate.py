"""Aggregate the Pchilka POS simulation export into small CSVs for the local DB import.

Usage: python -I -X utf8 aggregate.py <export_dir> <out_dir> [shop_code,shop_code,...]

Reads receipts.csv + receipt_items.csv (skips cash_events.csv, 9 GB of device errors).
Writes: locations.csv, items.csv, daily_sales.csv (net of returns, per shop/product/day).
Default shops = the 3 with the most receipts.
"""
import csv
import sys
from collections import Counter, defaultdict
from pathlib import Path

# The POS export mixes Latin look-alike letters into Cyrillic words ("Cир" with a Latin C, "Ємнiсть"
# with a Latin i), which breaks alphabetical sorting. Fix only tokens that already contain Cyrillic,
# so genuine Latin brand names ("Rokiskio", "MacCoffee") are untouched.
_CYR = set("АБВГҐДЕЄЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЬЮЯабвгґдеєжзиіїйклмнопрстуфхцчшщьюя")
_LOOK = str.maketrans("ABCEHIKMOPTXaceopxyi", "АВСЕНІКМОРТХасеорхуі")


def fix_lookalikes(name: str) -> str:
    return " ".join(
        tok.translate(_LOOK) if any(ch in _CYR for ch in tok) else tok
        for tok in name.split(" ")
    )


src, out = Path(sys.argv[1]), Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)
wanted = set(sys.argv[3].split(',')) if len(sys.argv) > 3 else None

# Pass 1: receipts -> shop names, receipt date per receipt_id
shops, counts, rdate, rtx = {}, Counter(), {}, {}
rcl = []                            # (receipt_id, loyalty client code)
txi = []                            # (receipt_id, product_code, net qty, unit_price, discount)
with open(src / 'receipts.csv', encoding='utf-8', newline='') as f:
    for r in csv.DictReader(f):
        counts[r['shop_code']] += 1
        shops[r['shop_code']] = r['shop_name']
if wanted is None:
    wanted = {s for s, _ in counts.most_common(3)}
with open(src / 'receipts.csv', encoding='utf-8', newline='') as f:
    for r in csv.DictReader(f):
        if r['shop_code'] in wanted and r['order_type'] == '0':
            # Some receipts have an empty receipt_at — fall back to the order date.
            d = (r['receipt_at'] or r['ordered_at'])[:10]
            if d:
                rdate[r['receipt_id']] = d
                rtx[r['receipt_id']] = (r['shop_code'], r['receipt_at'] or r['ordered_at'],
                                        r['receipt_total'], r['receipt_discount_total'])
                if r['loyalty_client_code']:
                    rcl.append((r['receipt_id'], r['loyalty_client_code']))

# Pass 2: items -> daily net quantity + item master
sales = defaultdict(float)          # (shop, product, date) -> qty
items = {}                          # product_code -> row
price_sum, price_n = defaultdict(float), Counter()
with open(src / 'receipt_items.csv', encoding='utf-8', newline='') as f:
    for r in csv.DictReader(f):
        d = rdate.get(r['receipt_id'])
        if d is None:
            continue
        q = float(r['quantity'] or 0) - float(r['returned_quantity'] or 0)
        sales[(r['shop_code'], r['product_code'], d)] += q
        pc = r['product_code']
        if q > 0:
            txi.append((r['receipt_id'], pc, round(q, 3), r['unit_price'], r['line_discount_total']))
        if pc not in items:
            items[pc] = (r['product_name'], r['unit_code'], r['barcode'])
        price_sum[pc] += float(r['unit_price'] or 0)
        price_n[pc] += 1

with open(out / 'locations.csv', 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f); w.writerow(['shop_code', 'name'])
    for s in sorted(wanted):
        w.writerow([s, shops[s]])
with open(out / 'items.csv', 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f); w.writerow(['product_code', 'name', 'unit', 'barcode', 'price'])
    for pc, (name, unit, bc) in items.items():
        w.writerow([pc, fix_lookalikes(name)[:255], 'кг' if unit == '2' else 'шт', bc, round(price_sum[pc] / price_n[pc], 2)])
n = 0
with open(out / 'daily_sales.csv', 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f); w.writerow(['shop_code', 'product_code', 'date', 'qty'])
    for (s, p, d), q in sales.items():
        if q > 0:
            w.writerow([s, p, d, round(q, 2)]); n += 1
with open(out / 'transactions.csv', 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f); w.writerow(['receipt_id', 'shop_code', 'receipt_at', 'total', 'discount'])
    for rid, (sc, ts, tot, disc) in rtx.items():
        w.writerow([rid, sc, ts, tot, disc])
with open(out / 'tx_clients.csv', 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f); w.writerow(['receipt_id', 'client_code'])
    w.writerows(rcl)
with open(out / 'tx_items.csv', 'w', encoding='utf-8', newline='') as f:
    w = csv.writer(f); w.writerow(['receipt_id', 'product_code', 'qty', 'unit_price', 'discount'])
    w.writerows(txi)
print(f'shops={sorted(wanted)} items={len(items)} daily_sales_rows={n}')
