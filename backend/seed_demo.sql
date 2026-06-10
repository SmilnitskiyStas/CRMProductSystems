-- ============================================================
-- ShelfGuard Demo Seed
-- Tenant: Свіжий Кут (8abfbbb5-3190-4de9-9f91-f4de59101bca)
-- Дата: 2026-06-06
-- ============================================================
SET row_security = off;

BEGIN;

DO $$
DECLARE
  tid  UUID := '8abfbbb5-3190-4de9-9f91-f4de59101bca';
  ea   UUID := '790e6610-dfe3-43f5-94b8-84a0f4541ccf';
  nm   UUID := 'ae46319d-014c-4641-a9e5-1f1a62a1e065';
  sm   UUID := 'a40f8c49-bbe1-49e2-a6ea-cdbb70471f25';
  sk   UUID := 'fcafcfd1-5efb-43f2-a99f-af73d94093b6';
  m1   UUID := '69c83849-bd33-420b-a16a-2b3aa3f768a2';
  m2   UUID := '9d5a418f-9113-4d2b-a70c-41c3e6fc68ee';

  s1   UUID := gen_random_uuid();
  s2   UUID := gen_random_uuid();

  z1d  UUID := gen_random_uuid();
  z1m  UUID := gen_random_uuid();
  z1g  UUID := gen_random_uuid();
  z1p  UUID := gen_random_uuid();
  z1b  UUID := gen_random_uuid();
  z2m  UUID := gen_random_uuid();
  z2c  UUID := gen_random_uuid();

  sup1 UUID := gen_random_uuid();
  sup2 UUID := gen_random_uuid();
  sup3 UUID := gen_random_uuid();

  cp1  UUID := gen_random_uuid();
  cp2  UUID := gen_random_uuid();
  cp3  UUID := gen_random_uuid();
  cp4  UUID := gen_random_uuid();
  cp5  UUID := gen_random_uuid();
  cp6  UUID := gen_random_uuid();
  cp7  UUID := gen_random_uuid();
  cp8  UUID := gen_random_uuid();
  cp9  UUID := gen_random_uuid();
  cp10 UUID := gen_random_uuid();
  cp11 UUID := gen_random_uuid();
  cp12 UUID := gen_random_uuid();
  cp13 UUID := gen_random_uuid();
  cp14 UUID := gen_random_uuid();
  cp15 UUID := gen_random_uuid();

  sb1  UUID := gen_random_uuid(); sb2  UUID := gen_random_uuid();
  sb3  UUID := gen_random_uuid(); sb4  UUID := gen_random_uuid();
  sb5  UUID := gen_random_uuid(); sb6  UUID := gen_random_uuid();
  sb7  UUID := gen_random_uuid(); sb8  UUID := gen_random_uuid();
  sb9  UUID := gen_random_uuid(); sb10 UUID := gen_random_uuid();
  sb11 UUID := gen_random_uuid(); sb12 UUID := gen_random_uuid();
  sb13 UUID := gen_random_uuid(); sb14 UUID := gen_random_uuid();
  sb15 UUID := gen_random_uuid(); sb16 UUID := gen_random_uuid();
  sb17 UUID := gen_random_uuid(); sb18 UUID := gen_random_uuid();
  sb19 UUID := gen_random_uuid(); sb20 UUID := gen_random_uuid();
  sb21 UUID := gen_random_uuid(); sb22 UUID := gen_random_uuid();
  sb23 UUID := gen_random_uuid(); sb24 UUID := gen_random_uuid();
  sb25 UUID := gen_random_uuid();

  r1 UUID := gen_random_uuid(); r2 UUID := gen_random_uuid();
  r3 UUID := gen_random_uuid(); r4 UUID := gen_random_uuid();
  tr1 UUID := gen_random_uuid(); tr2 UUID := gen_random_uuid();
  w1 UUID := gen_random_uuid(); w2 UUID := gen_random_uuid();
  w3 UUID := gen_random_uuid();

BEGIN

  -- ── STORES ──────────────────────────────────────────────
  INSERT INTO stores ("Id","TenantId","Name","Address","Type","IsActive","CreatedAt") VALUES
    (s1, tid, 'Свіжий Кут Центральний', 'вул. Хрещатик, 15, Київ',    'shop', true, NOW()-'180 days'::interval),
    (s2, tid, 'Свіжий Кут Подільський', 'вул. Сагайдачного, 42, Київ', 'shop', true, NOW()-'90 days'::interval);

  -- ── ZONES ───────────────────────────────────────────────
  INSERT INTO store_zones ("Id","StoreId","Name","Type","ShelvesCount","TempMin","TempMax","IsActive") VALUES
    (z1d, s1, 'Молочний відділ',   'refrigerated', 4, 2.0, 6.0, true),
    (z1m, s1, 'М''ясний відділ',   'refrigerated', 3, 0.0, 4.0, true),
    (z1g, s1, 'Бакалія',           'shelf',        8, null, null, true),
    (z1p, s1, 'Фрукти та овочі',   'fresh',        5, null, null, true),
    (z1b, s1, 'Напої',             'shelf',        4, null, null, true),
    (z2m, s2, 'Основний зал',      'shelf',       10, null, null, true),
    (z2c, s2, 'Холодильний відділ','refrigerated', 3, 2.0, 6.0, true);

  -- ── SUPPLIERS ───────────────────────────────────────────
  INSERT INTO suppliers
    ("Id","TenantId","Name","Edrpou","ContactPerson","Phone","Email","DeliveryDays","HasSupplierPortal","ReturnPolicy","PaymentTerms","IsActive")
  VALUES
    (sup1,tid,'Молокія ТОВ',          '12345678','Сергій Даниленко', '+380441112233','orders@molokiya.ua', 2, true,  true,  'net14',  true),
    (sup2,tid,'АгроФреш ФГ',          '87654321','Наталія Ільченко', '+380442223344','fresh@agrofresh.ua', 1, false, false, 'prepay', true),
    (sup3,tid,'Чумак Дістрибуція ТОВ','11223344','Василь Приходько', '+380443334455','b2b@chumak.ua',      3, true,  true,  'net30',  true);

  -- ── CATALOG PRODUCTS ────────────────────────────────────
  INSERT INTO catalog_products
    ("Id","TenantId","Barcode","Name","Unit","ManagementType","MinStock","MaxStock","SafetyBuffer",
     "StorageTempMin","StorageTempMax","ShelfLifeDays","DefaultSupplierId","VatRate","PricePurchase","PriceRetail","IsActive","CreatedAt")
  VALUES
    (cp1, tid,'4820029300128','Молоко 2,5% Галичина 1л',      'л',  'MTS', 20,120,10, 2.0,6.0,  7, sup1,20, 18.50, 24.90,true,NOW()),
    (cp2, tid,'4823089701006','Кефір 1% Простоквашино 1л',    'л',  'MTS', 15, 80, 8, 2.0,6.0,  7, sup1,20, 16.00, 21.50,true,NOW()),
    (cp3, tid,'4820000123456','Сметана 20% Президент 350г',    'кг', 'MTS', 10, 50, 5, 2.0,6.0, 14, sup1,20, 42.00, 55.00,true,NOW()),
    (cp4, tid,'4820033440012','Масло вершкове 82,5% 200г',     'кг', 'MTS',  8, 40, 4, 2.0,6.0, 30, sup1,20,168.00,215.00,true,NOW()),
    (cp5, tid,'4820060300157','Сир кисломолочний 9% 400г',     'кг', 'MTS', 10, 60, 5, 2.0,6.0,  5, sup1,20, 85.00,110.00,true,NOW()),
    (cp6, tid,'4823000000601','Помідори cherry 500г',           'кг', 'MTS', 15, 80,10, null,null, 7, sup2,20, 45.00, 65.00,true,NOW()),
    (cp7, tid,'4823000000702','Огірки свіжі кг',                'кг', 'MTS', 20, 90,12, null,null, 7, sup2,20, 28.00, 40.00,true,NOW()),
    (cp8, tid,'4823001234568','Куряче філе охолоджене кг',      'кг', 'MTS', 15, 60, 8, 0.0,4.0,  5, sup2,20, 95.00,129.00,true,NOW()),
    (cp9, tid,'4823009876543','Ковбаса Докторська Ятрань кг',   'кг', 'MTS',  8, 40, 4, 0.0,6.0, 15, sup1,20,115.00,149.00,true,NOW()),
    (cp10,tid,'4823050100104','Рис круглозернистий Чумак 1кг',  'кг', 'MTS', 40,200,20, null,null,730,sup3,20, 22.00, 32.00,true,NOW()),
    (cp11,tid,'4823050200201','Гречка ядриця Жменька 1кг',      'кг', 'MTS', 30,180,15, null,null,730,sup3,20, 35.00, 49.00,true,NOW()),
    (cp12,tid,'4823050300301','Олія соняшникова Чумак 1л',      'л',  'MTS', 20,100,10, null,null,365,sup3,20, 48.00, 65.00,true,NOW()),
    (cp13,tid,'4820000130013','Вода Моршинська 1,5л',            'шт', 'MTS', 48,200,24, null,null,365,sup3,20,  8.50, 14.00,true,NOW()),
    (cp14,tid,'4820000140014','Сік Садочок яблуко 1л',           'шт', 'MTS', 24,120,12, null,null,180,sup3,20, 22.00, 31.00,true,NOW()),
    (cp15,tid,'4823000001501','Хліб Дарницький Київхліб',        'шт', 'MTS', 15, 60, 8, null,null,  3,sup2,20, 14.00, 22.00,true,NOW());

  -- ── PRODUCT STOCK ────────────────────────────────────────
  INSERT INTO product_stock
    ("Id","TenantId","ProductId","StoreId","ZoneId","ShelfNumber","BatchNumber",
     "Quantity","QuantityInitial","ExpiryDate","Status","SourceType","AddedBy","AddedAt","LastCheckedAt")
  VALUES
    (sb1, tid,cp1, s1,z1d,1,'MLK-2026-051', 45,45, '2026-06-25','warning',  'receipt',sk,NOW()-'10 days'::interval,NOW()-'5 days'::interval),
    (sb2, tid,cp1, s1,z1d,1,'MLK-2026-052', 60,60, '2026-07-20','safe',     'receipt',sk,NOW()-'3 days'::interval, NOW()),
    (sb3, tid,cp2, s1,z1d,2,'KEF-2026-051', 30,30, '2026-06-10','critical', 'receipt',sk,NOW()-'7 days'::interval, NOW()-'2 days'::interval),
    (sb4, tid,cp2, s1,z1d,2,'KEF-2026-052', 40,40, '2026-07-10','safe',     'receipt',sk,NOW()-'3 days'::interval, NOW()),
    (sb5, tid,cp3, s1,z1d,3,'SME-2026-051', 12,12, '2026-07-04','warning',  'receipt',sk,NOW()-'7 days'::interval, NOW()-'1 day'::interval),
    (sb6, tid,cp4, s1,z1d,4,'MAS-2026-051',  8, 8, '2026-06-04','expired',  'receipt',sk,NOW()-'14 days'::interval,NOW()-'3 days'::interval),
    (sb7, tid,cp5, s1,z1d,4,'SYR-2026-051', 18,18, '2026-06-08','critical', 'receipt',sk,NOW()-'5 days'::interval, NOW()-'1 day'::interval),
    (sb8, tid,cp5, s1,z1d,4,'SYR-2026-052', 24,24, '2026-07-15','safe',     'receipt',sk,NOW()-'2 days'::interval, NOW()),
    (sb9, tid,cp8, s1,z1m,1,'KUR-2026-051', 20,20, '2026-06-09','critical', 'receipt',sk,NOW()-'4 days'::interval, NOW()-'1 day'::interval),
    (sb10,tid,cp8, s1,z1m,1,'KUR-2026-052', 35,35, '2026-06-20','warning',  'receipt',sk,NOW()-'2 days'::interval, NOW()),
    (sb11,tid,cp9, s1,z1m,2,'KOV-2026-051', 15,15, '2026-07-01','warning',  'receipt',sk,NOW()-'5 days'::interval, NOW()-'2 days'::interval),
    (sb12,tid,cp9, s1,z1m,2,'KOV-2026-052', 22,22, '2026-08-01','safe',     'receipt',sk,NOW()-'1 day'::interval, NOW()),
    (sb13,tid,cp10,s1,z1g,1,'RYS-2026-011',120,120,'2027-06-01','safe',     'receipt',sk,NOW()-'30 days'::interval,NOW()-'10 days'::interval),
    (sb14,tid,cp11,s1,z1g,2,'GRE-2026-011', 95,95, '2027-05-01','safe',     'receipt',sk,NOW()-'25 days'::interval,NOW()-'7 days'::interval),
    (sb15,tid,cp12,s1,z1g,3,'OLI-2026-011', 48,48, '2027-03-15','safe',     'receipt',sk,NOW()-'20 days'::interval,NOW()-'5 days'::interval),
    (sb16,tid,cp6, s1,z1p,1,'TOM-2026-051', 30,30, '2026-06-11','critical', 'receipt',sk,NOW()-'3 days'::interval, NOW()-'1 day'::interval),
    (sb17,tid,cp6, s1,z1p,1,'TOM-2026-052', 45,45, '2026-06-20','warning',  'receipt',sk,NOW()-'1 day'::interval, NOW()),
    (sb18,tid,cp7, s1,z1p,2,'OGI-2026-051', 60,60, '2026-06-25','warning',  'receipt',sk,NOW()-'2 days'::interval, NOW()),
    (sb19,tid,cp13,s1,z1b,1,'VDA-2026-011',144,144,'2027-06-01','safe',     'receipt',sk,NOW()-'10 days'::interval,NOW()-'3 days'::interval),
    (sb20,tid,cp14,s1,z1b,2,'SOK-2026-011', 48,48, '2027-01-15','safe',     'receipt',sk,NOW()-'10 days'::interval,NOW()-'3 days'::interval),
    (sb21,tid,cp1, s2,z2c,1,'MLK-2026-053', 30,30, '2026-06-05','expired',  'receipt',sk,NOW()-'15 days'::interval,NOW()-'5 days'::interval),
    (sb22,tid,cp1, s2,z2c,1,'MLK-2026-054', 40,40, '2026-07-25','safe',     'receipt',sk,NOW()-'2 days'::interval, NOW()),
    (sb23,tid,cp8, s2,z2c,2,'KUR-2026-053', 25,25, '2026-06-22','warning',  'receipt',sk,NOW()-'3 days'::interval, NOW()),
    (sb24,tid,cp10,s2,z2m,1,'RYS-2026-012', 80,80, '2027-04-01','safe',     'receipt',sk,NOW()-'20 days'::interval,NOW()-'5 days'::interval),
    (sb25,tid,cp15,s2,z2m,3,'BRD-2026-051',  8, 8, '2026-06-07','critical', 'receipt',sk,NOW()-'1 day'::interval, NOW());

  -- ── RECEIPTS ─────────────────────────────────────────────
  INSERT INTO stock_receipts
    ("Id","TenantId","SupplierId","DestinationStoreId","ViaCentralStore","Status","ExpectedAt","ReceivedAt","CreatedBy","ReceivedBy","Notes","CreatedAt")
  VALUES
    (r1,tid,sup1,s1,false,'received',  '2026-05-28','2026-05-28 10:30:00+03',sm,sk,'Планова доставка молочних продуктів',NOW()-'9 days'::interval),
    (r2,tid,sup2,s1,false,'in_transit','2026-06-08', null,                   sm,null,'Свіжі овочі та фрукти — термінова поставка',NOW()-'2 days'::interval),
    (r3,tid,sup3,s1,false,'draft',     '2026-06-15', null,                   sm,null,'Квартальне замовлення бакалії',NOW()-'1 day'::interval),
    (r4,tid,sup1,s2,false,'received',  '2026-05-25','2026-05-25 14:00:00+03',nm,sk,'Відкриття нового відділу — початковий запас',NOW()-'12 days'::interval);

  INSERT INTO stock_receipt_items
    ("Id","ReceiptId","ProductId","QuantityOrdered","QuantityReceived","PricePurchase","ExpiryDate","BatchNumber")
  VALUES
    (gen_random_uuid(),r1,cp1, 60,  60, 18.50,'2026-07-20','MLK-2026-052'),
    (gen_random_uuid(),r1,cp2, 40,  40, 16.00,'2026-07-10','KEF-2026-052'),
    (gen_random_uuid(),r1,cp3, 12,  12, 42.00,'2026-07-04','SME-2026-051'),
    (gen_random_uuid(),r1,cp4,  8,   8,168.00,'2026-06-04','MAS-2026-051'),
    (gen_random_uuid(),r1,cp5, 24,  24, 85.00,'2026-07-15','SYR-2026-052'),
    (gen_random_uuid(),r2,cp6, 50,null, 45.00,'2026-06-20','TOM-2026-052'),
    (gen_random_uuid(),r2,cp7, 70,null, 28.00,'2026-06-25','OGI-2026-051'),
    (gen_random_uuid(),r3,cp10,150,null,22.00, null, null),
    (gen_random_uuid(),r3,cp11,100,null,35.00, null, null),
    (gen_random_uuid(),r3,cp12, 60,null,48.00, null, null),
    (gen_random_uuid(),r3,cp13,200,null, 8.50, null, null),
    (gen_random_uuid(),r4,cp1, 40,  40, 18.50,'2026-07-25','MLK-2026-054'),
    (gen_random_uuid(),r4,cp8, 25,  25, 95.00,'2026-06-22','KUR-2026-053');

  -- ── TRANSFERS ────────────────────────────────────────────
  INSERT INTO stock_transfers
    ("Id","TenantId","FromStoreId","ToStoreId","TransferType","Status","InitiatedBy","ConfirmedBy","Notes","CreatedAt")
  VALUES
    (tr1,tid,s1,s2,'manual','received',  sm,sk,  'Поповнення Подільського — молочка',       NOW()-'5 days'::interval),
    (tr2,tid,s1,s2,'manual','in_transit',nm,null,'Термінове переміщення залишків бакалії',  NOW()-'1 day'::interval);

  INSERT INTO stock_transfer_items
    ("Id","TransferId","ProductStockId","ProductId","Quantity","ExpiryDate","BatchNumber")
  VALUES
    (gen_random_uuid(),tr1,sb2, cp1,15,'2026-07-20','MLK-2026-052'),
    (gen_random_uuid(),tr1,sb4, cp2,10,'2026-07-10','KEF-2026-052'),
    (gen_random_uuid(),tr2,sb13,cp10,30,'2027-06-01','RYS-2026-011'),
    (gen_random_uuid(),tr2,sb15,cp12,12,'2027-03-15','OLI-2026-011');

  -- ── WRITE-OFFS ───────────────────────────────────────────
  INSERT INTO write_offs
    ("Id","TenantId","StoreId","Status","Reason","TotalLossAmount","CreatedBy","ApprovedBy","CreatedAt","ApprovedAt")
  VALUES
    (w1,tid,s1,'approved',         'expired', 2139.00,m1,sm,NOW()-'3 days'::interval,NOW()-'2 days'::interval),
    (w2,tid,s1,'pending_approval', 'damaged',  955.00,m1,null,NOW()-'1 day'::interval,null),
    (w3,tid,s2,'draft',            'theft',    520.00,sk,null,NOW(),null);

  INSERT INTO write_off_items
    ("Id","WriteOffId","ProductStockId","ProductId","Quantity","UnitPrice","LossAmount")
  VALUES
    (gen_random_uuid(),w1,sb6, cp4,  8,168.00,1344.00),
    (gen_random_uuid(),w1,sb3, cp2, 15, 16.00, 240.00),
    (gen_random_uuid(),w1,sb21,cp1, 30, 18.50, 555.00),
    (gen_random_uuid(),w2,sb16,cp6, 15, 45.00, 675.00),
    (gen_random_uuid(),w2,sb18,cp7, 10, 28.00, 280.00),
    (gen_random_uuid(),w3,sb24,cp10,20, 22.00, 440.00),
    (gen_random_uuid(),w3,sb24,cp12, 2, 48.00,  96.00);

  -- ── USERS → STORES ───────────────────────────────────────
  UPDATE users SET "StoreId" = s1 WHERE "Id" IN (sm, sk, m1);
  UPDATE users SET "StoreId" = s2 WHERE "Id" = m2;

  RAISE NOTICE 'Seed done: 2 stores, 7 zones, 3 suppliers, 15 products, 25 batches, 4 receipts, 2 transfers, 3 write-offs';
END;
$$;

COMMIT;
SET row_security = on;
