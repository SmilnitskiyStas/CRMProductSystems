-- Loyalty programme demo data for the pchilka-pos-sim tenant (local dev only; run after clients.sql).
-- The export has no mobile-app data, so this SIMULATES an app audience: the top 1500 customers by spend
-- get a consumer account + loyalty membership (card, balance, tier), and their receipts are linked to
-- the membership. Tiers use simple spend thresholds. Nothing about app content views / banner taps is
-- invented — those event tables stay empty. Idempotent: rebuilds this tenant's loyalty rows.
\set ON_ERROR_STOP on
BEGIN;
CREATE TEMP TABLE t AS SELECT "Id" AS tid FROM tenants WHERE "Slug"='pchilka-pos-sim';

-- clean previous run
UPDATE pos_transactions SET "LoyaltyMembershipId" = NULL WHERE "TenantId" = (SELECT tid FROM t);
DELETE FROM loyalty_ledger_entries WHERE "TenantId" = (SELECT tid FROM t);
CREATE TEMP TABLE old_acc AS
  SELECT "ConsumerAccountId" AS id FROM loyalty_memberships WHERE "TenantId" = (SELECT tid FROM t);
DELETE FROM loyalty_memberships WHERE "TenantId" = (SELECT tid FROM t);
DELETE FROM consumer_accounts WHERE "Id" IN (SELECT id FROM old_acc);
DELETE FROM loyalty_tier_definitions WHERE "TenantId" = (SELECT tid FROM t);
DELETE FROM loyalty_program_settings WHERE "TenantId" = (SELECT tid FROM t);

INSERT INTO loyalty_program_settings ("TenantId","IsEnabled","AccrualRatePercent","RedemptionCapPercent")
  SELECT tid, true, 3, 50 FROM t;

INSERT INTO loyalty_tier_definitions ("TenantId","Name","SortOrder","MinCompositeScore","AccrualMultiplier","DiscountPercent","Description")
  SELECT (SELECT tid FROM t), v.name, v.ord, v.min_score, v.mult, v.disc, v.descr
  FROM (VALUES
    ('Бронза',  1,     0::numeric, 1.00::numeric, 0::numeric, 'Базовий рівень'),
    ('Срібло',  2,  3000, 1.20, 1, 'Від 3 000 ₴ покупок'),
    ('Золото',  3, 10000, 1.50, 2, 'Від 10 000 ₴ покупок'),
    ('Платина', 4, 30000, 2.00, 3, 'Від 30 000 ₴ покупок')
  ) AS v(name, ord, min_score, mult, disc, descr);

CREATE TEMP TABLE pick AS
  SELECT c."Id" AS cid, c."Name" AS name, c."CreatedAt" AS joined, c."TotalOrders" AS orders, c."TotalSpent" AS spent,
         ROW_NUMBER() OVER (ORDER BY c."TotalSpent" DESC, c."Id") AS rn,
         gen_random_uuid() AS acc_id, gen_random_uuid() AS mem_id
  FROM customers c
  WHERE c."TenantId" = (SELECT tid FROM t) AND c."TotalOrders" > 0
  ORDER BY c."TotalSpent" DESC, c."Id"
  LIMIT 1500;

INSERT INTO consumer_accounts ("Id","Phone","PasswordHash","FullName","CreatedAt","LastLoginAt")
  SELECT acc_id, '+380900' || lpad(rn::text, 6, '0'),
         (SELECT "PasswordHash" FROM users WHERE "Email" = 'manager@pchilka.local'),
         name, joined, now() - (rn % 20) * interval '1 day'
  FROM pick;

-- most frequent store per customer
CREATE TEMP TABLE fav AS
  SELECT DISTINCT ON (tx."CustomerId") tx."CustomerId" AS cid, tx."LocationId" AS store_id
  FROM (SELECT "CustomerId","LocationId", count(*) n FROM pos_transactions
        WHERE "TenantId" = (SELECT tid FROM t) AND "CustomerId" IS NOT NULL GROUP BY 1,2) tx
  ORDER BY tx."CustomerId", tx.n DESC;

INSERT INTO loyalty_memberships
  ("Id","TenantId","ConsumerAccountId","CustomerId","TotpSecret","Balance","Status","JoinedAt","PreferredStoreId",
   "CompositeScore","CurrentTierId","TierScoreUpdatedAt","TierCashSpend","TierEarnedBonuses","TierMembershipDays",
   "TierPurchaseCount")
  SELECT p.mem_id, (SELECT tid FROM t), p.acc_id, p.cid, 'sim-secret',
         ROUND(p.spent * 0.03 * 0.4, 2), 'active', p.joined, f.store_id,
         p.spent,
         (SELECT d."Id" FROM loyalty_tier_definitions d
           WHERE d."TenantId" = (SELECT tid FROM t) AND d."MinCompositeScore" <= p.spent
           ORDER BY d."SortOrder" DESC LIMIT 1),
         now(), p.spent, ROUND(p.spent * 0.03, 2),
         GREATEST(0, (CURRENT_DATE - p.joined::date)), p.orders
  FROM pick p LEFT JOIN fav f ON f.cid = p.cid;

INSERT INTO loyalty_ledger_entries ("TenantId","MembershipId","EntryType","Amount","BalanceAfter","Note","CreatedAt")
  SELECT (SELECT tid FROM t), p.mem_id, 'accrual', ROUND(p.spent * 0.03 * 0.4, 2), ROUND(p.spent * 0.03 * 0.4, 2),
         'Нарахування за покупки (симуляція)', now()
  FROM pick p;

UPDATE pos_transactions tx SET "LoyaltyMembershipId" = p.mem_id
FROM pick p
WHERE tx."TenantId" = (SELECT tid FROM t) AND tx."CustomerId" = p.cid;
COMMIT;

SELECT (SELECT count(*) FROM loyalty_memberships WHERE "TenantId" = (SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')) memberships,
       (SELECT count(*) FROM pos_transactions WHERE "TenantId" = (SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim')
          AND "LoyaltyMembershipId" IS NOT NULL) linked_receipts;
SELECT d."Name", count(*) FROM loyalty_memberships m JOIN loyalty_tier_definitions d ON d."Id" = m."CurrentTierId"
WHERE m."TenantId" = (SELECT "Id" FROM tenants WHERE "Slug"='pchilka-pos-sim') GROUP BY 1 ORDER BY 2 DESC;
