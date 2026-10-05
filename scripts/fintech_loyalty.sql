-- ============================================================
-- FinPay Loyalty — Fintech Örnek Tenant Seed
-- Tenant ID : fintech
-- ============================================================
-- Senaryo (kart harcaması → puan → cashback):
--   • Her 1 TL kart harcaması = 1 FinPuan (%1 geri kazanım)
--   • QR ile ödeme 2x, yemek kategorisi 3x (exclusive — en yüksek priority kazanır)
--   • Gold üyeye +%25, Platinum üyeye +%50 ekstra puan (stackable, tier koşullu)
--   • Hoş geldin +1000 FinPuan (ömür boyu 1 kez — per_customer_total)
--   • 100 FinPuan = 1 TL; puanlar cashback bakiyesine dönüştürülür (points.redeem)
--   • Puanla kupon satın alma: 1000 puan = 10 TL, 4500 puan = 50 TL kupon
--   • Puanlar 180 günde yanar; 14 gün önce expiring uyarısı üretilir
--   • Tier: dönemsel 365 gün + 30 gün grace (standard / gold / platinum)
-- ============================================================
-- Çalıştırma:
--   docker exec -i loyalty-postgres-1 psql -U postgres -d loyalty_dev < scripts/fintech_loyalty.sql
-- ============================================================

BEGIN;

-- --------------------------------------------------------
-- 0. Partition — her yeni tenant için bir kez çalıştırılır
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS ledger_entries_fintech
    PARTITION OF ledger_entries FOR VALUES IN ('fintech');

CREATE TABLE IF NOT EXISTS event_inbox_fintech
    PARTITION OF event_inbox FOR VALUES IN ('fintech');

-- --------------------------------------------------------
-- 1. Program
--    warning_days = 14 → expiry'den 14 gün önce loyalty.points.expiring
-- --------------------------------------------------------
INSERT INTO programs (id, tenant_id, name, status, warning_days, created_at)
VALUES (
    '019fd001-0000-7000-8000-000000000001',
    'fintech',
    'FinPay Rewards',
    'active',
    14,
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- --------------------------------------------------------
-- 2. Account Types
-- --------------------------------------------------------

-- 2a. POINTS — FinPuan
--   Redemption: 100 FinPuan = 1 TL cashback (rate: 0.01, min: 1000 puan = 10 TL)
--   Expiry: 180 gün (kazanım tarihinden itibaren, FIFO)
INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at)
VALUES (
    '019fd002-0000-7000-8000-000000000001',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'POINTS',
    'FinPuan',
    '{
        "expiration_days": 180,
        "redemption": {
            "target_account_type_id": "019fd002-0000-7000-8000-000000000002",
            "rate": 0.01,
            "min_points": 1000
        }
    }',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- 2b. CASH — Cashback Bakiye
--   points.redeem'in hedefi; cash.spent ile harcanır, negatife düşemez
INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at)
VALUES (
    '019fd002-0000-7000-8000-000000000002',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'CASH',
    'Cashback Bakiye',
    '{"currency": "TRY"}',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- --------------------------------------------------------
-- 3. Tier Tanımları
--    qualifying_account_type_id = FinPuan (POINTS)
--    Model: dönemsel 365 gün + 30 gün grace
-- --------------------------------------------------------

INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '019fd004-0000-7000-8000-000000000001',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'standard',
    'Standart',
    0,
    365,
    30,
    1,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '019fd004-0000-7000-8000-000000000002',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'gold',
    'Gold',
    5000,
    365,
    30,
    2,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '019fd004-0000-7000-8000-000000000003',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'platinum',
    'Platinum',
    25000,
    365,
    30,
    3,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

-- --------------------------------------------------------
-- 4. Program — qualifying_account_type_id bağla
-- --------------------------------------------------------
UPDATE programs
SET qualifying_account_type_id = '019fd002-0000-7000-8000-000000000001'
WHERE id = '019fd001-0000-7000-8000-000000000001'
  AND qualifying_account_type_id IS NULL;

-- --------------------------------------------------------
-- 5. Rules
-- --------------------------------------------------------

-- Rule 1: Temel Kazanım — 1 TL = 1 FinPuan (%1 cashback tabanı)
--   Günlük tavan: 10.000 puan (100 TL değer)
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '019fd003-0000-7000-8000-000000000001',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'Temel Kazanım — 1 TL = 1 FinPuan',
    'SpendRule',
    'order.created',
    NULL,
    '{"type": "spend", "rate": 1.00}',
    '019fd002-0000-7000-8000-000000000001',
    '{"per_customer_per_day": 10000}',
    10,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 2: QR Ödeme 2x FinPuan (exclusive — temel kuralı ezer)
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '019fd003-0000-7000-8000-000000000002',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'QR Ödeme 2x FinPuan',
    'SpendRule',
    'order.created',
    '[{"field": "payment_method", "op": "eq", "value": "qr"}]',
    '{"type": "spend", "rate": 2.00}',
    '019fd002-0000-7000-8000-000000000001',
    NULL,
    20,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 3: Yemek Kategorisi 3x FinPuan (exclusive — en yüksek priority)
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '019fd003-0000-7000-8000-000000000003',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'Yemek Kategorisi 3x FinPuan',
    'SpendRule',
    'order.created',
    '[{"field": "items.category", "op": "in", "value": ["dining", "restaurant"]}]',
    '{"type": "spend", "rate": 3.00}',
    '019fd002-0000-7000-8000-000000000001',
    NULL,
    30,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 4: Hoş Geldin +1000 FinPuan — ömür boyu 1 kez
--   stackable: true (kazanan kuralın üstüne eklenir)
--   per_customer_total 1000 → ikinci siparişte 0'a kırpılır (one-shot)
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '019fd003-0000-7000-8000-000000000004',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'Hoş Geldin +1000 FinPuan',
    'FixedBonusRule',
    'order.created',
    NULL,
    '{"type": "fixed", "amount": 1000}',
    '019fd002-0000-7000-8000-000000000001',
    '{"per_customer_total": 1000}',
    5,
    true,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 5: Gold Ekstra %25 — tier koşullu stackable çarpan
--   tier × earning: ayrı mekanizma yok, koşullu stackable kural yeterli
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '019fd003-0000-7000-8000-000000000005',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'Gold Ekstra %25',
    'SpendRule',
    'order.created',
    '[{"field": "tier", "op": "in", "value": ["gold"]}]',
    '{"type": "spend", "rate": 0.25}',
    '019fd002-0000-7000-8000-000000000001',
    NULL,
    40,
    true,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 6: Platinum Ekstra %50 — en üst tier daha fazla kazanır
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '019fd003-0000-7000-8000-000000000006',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'Platinum Ekstra %50',
    'SpendRule',
    'order.created',
    '[{"field": "tier", "op": "in", "value": ["platinum"]}]',
    '{"type": "spend", "rate": 0.50}',
    '019fd002-0000-7000-8000-000000000001',
    NULL,
    50,
    true,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- --------------------------------------------------------
-- 6. Reward Tanımları
--    Kupon üretimi motorda değil — motor loyalty.reward.earned ile
--    external_coupon_type'ı dış sisteme bildirir.
-- --------------------------------------------------------

-- 6a. Puanla satın alma: 1000 FinPuan = 10 TL kupon (birebir kur)
INSERT INTO reward_definitions (
    id, tenant_id, program_id, name, display_name, acquisition,
    points_price, points_account_type_id,
    external_coupon_type, is_active, created_at
) VALUES (
    '019fd005-0000-7000-8000-000000000002',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'cashback_10',
    '10 TL Cashback Kuponu',
    'points_purchase',
    1000,
    '019fd002-0000-7000-8000-000000000001',
    'CASHBACK_CREDIT_10',
    true,
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- 6b. Puanla satın alma: 4500 FinPuan = 50 TL kupon (%10 avantajlı)
INSERT INTO reward_definitions (
    id, tenant_id, program_id, name, display_name, acquisition,
    points_price, points_account_type_id,
    external_coupon_type, is_active, created_at
) VALUES (
    '019fd005-0000-7000-8000-000000000003',
    'fintech',
    '019fd001-0000-7000-8000-000000000001',
    'cashback_50',
    '50 TL Cashback Kuponu',
    'points_purchase',
    4500,
    '019fd002-0000-7000-8000-000000000001',
    'CASHBACK_CREDIT_50',
    true,
    NOW()
)
ON CONFLICT (id) DO NOTHING;

COMMIT;

-- --------------------------------------------------------
-- Doğrulama
-- --------------------------------------------------------
SELECT 'PROGRAM'      AS entity, id::text, name, status                                        FROM programs           WHERE tenant_id = 'fintech'
UNION ALL
SELECT 'ACCOUNT_TYPE', id::text, name, type                                                    FROM account_types      WHERE tenant_id = 'fintech'
UNION ALL
SELECT 'TIER',         id::text, name || ' (' || display_name || ')', 'min=' || min_points::text FROM tier_definitions  WHERE tenant_id = 'fintech'
UNION ALL
SELECT 'RULE',         id::text, name, status                                                  FROM rules              WHERE tenant_id = 'fintech'
UNION ALL
SELECT 'REWARD',       id::text, name || ' → ' || external_coupon_type, acquisition            FROM reward_definitions WHERE tenant_id = 'fintech'
ORDER BY entity, name;
