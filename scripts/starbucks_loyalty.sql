-- ============================================================
-- Starbucks Loyalty — Örnek Tenant Seed
-- Tenant ID : starbucks
-- ============================================================
-- Çalıştırma:
--   docker exec -i loyalty-postgres-1 psql -U postgres -d loyalty_dev < scripts/starbucks_loyalty.sql
-- ============================================================

BEGIN;

-- --------------------------------------------------------
-- 0. Partition — her yeni tenant için bir kez çalıştırılır
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS ledger_entries_starbucks
    PARTITION OF ledger_entries FOR VALUES IN ('starbucks');

CREATE TABLE IF NOT EXISTS event_inbox_starbucks
    PARTITION OF event_inbox FOR VALUES IN ('starbucks');

-- --------------------------------------------------------
-- 1. Program
-- --------------------------------------------------------
INSERT INTO programs (id, tenant_id, name, status, created_at)
VALUES (
    '018fcd01-0000-7000-8000-000000000001',
    'starbucks',
    'Starbucks Rewards',
    'active',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- --------------------------------------------------------
-- 2. Account Types
-- --------------------------------------------------------

-- 2a. POINTS — Star (puan) hesabı
--   Redemption: 500 star = 50 TL cash (rate: 0.10, min: 500)
--   Expiry: 365 gün (puan kazanım tarihinden itibaren)
INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at)
VALUES (
    '018fcd02-0000-7000-8000-000000000001',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'POINTS',
    'Stars',
    '{
        "expiration_days": 365,
        "redemption": {
            "target_account_type_id": "018fcd02-0000-7000-8000-000000000002",
            "rate": 0.10,
            "min_points": 500
        }
    }',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- 2b. CASH — Starbucks Card (ön ödemeli bakiye)
--   Expire yok, negatife düşemez (uygulama seviyesinde kontrol)
INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at)
VALUES (
    '018fcd02-0000-7000-8000-000000000002',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'CASH',
    'Starbucks Card',
    '{}',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- 2c. STAMP — Kahve Damgası (10 damga = bedava içecek)
INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at)
VALUES (
    '018fcd02-0000-7000-8000-000000000003',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'STAMP',
    'Kahve Damgası',
    '{
        "stamp_target": 10,
        "reward_type": "free_drink"
    }',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- --------------------------------------------------------
-- 3. Tier Tanımları
--    qualifying_account_type_id = Stars (POINTS)
--    Qualifying model: dönemsel (365 gün)
-- --------------------------------------------------------

-- 3a. Yeşil Üye (en düşük tier — eşik: 0, her yeni üye buradan başlar)
INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '018fcd04-0000-7000-8000-000000000001',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'yesil',
    'Yeşil Üye',
    0,
    365,
    30,
    1,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

-- 3b. Altın Üye (eşik: 1000 qualifying puan)
INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '018fcd04-0000-7000-8000-000000000002',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'altin',
    'Altın Üye',
    1000,
    365,
    30,
    2,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

-- 3c. Siyah Üye (eşik: 5000 qualifying puan)
INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '018fcd04-0000-7000-8000-000000000003',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'siyah',
    'Siyah Üye',
    5000,
    365,
    30,
    3,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

-- --------------------------------------------------------
-- 4. Program — qualifying_account_type_id bağla
--    (account_types insert edildikten sonra güncellenir)
-- --------------------------------------------------------
UPDATE programs
SET qualifying_account_type_id = '018fcd02-0000-7000-8000-000000000001'
WHERE id = '018fcd01-0000-7000-8000-000000000001'
  AND qualifying_account_type_id IS NULL;

-- --------------------------------------------------------
-- 5. Rules
-- --------------------------------------------------------

-- Rule 1: Temel puan kazanma — her 10 TL = 1 Star
--   type: SpendRule, rate: 0.1, stackable: false, priority: 10
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fcd03-0000-7000-8000-000000000001',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'Temel Puan — 10 TL = 1 Star',
    'SpendRule',
    'order.created',
    NULL,
    '{"type": "spend", "rate": 0.10}',
    '018fcd02-0000-7000-8000-000000000001',
    '{"per_customer_per_day": 500}',
    10,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 2: Mobil kanaldan 2x Star
--   stackable: false, priority: 20
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fcd03-0000-7000-8000-000000000002',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'Mobil Kanal 2x Star',
    'SpendRule',
    'order.created',
    '[{"field": "channel", "op": "eq", "value": "mobile"}]',
    '{"type": "spend", "rate": 0.20}',
    '018fcd02-0000-7000-8000-000000000001',
    NULL,
    20,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 3: Kahve kategorisinden 1.5x Star
--   stackable: false, priority: 30
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fcd03-0000-7000-8000-000000000003',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'Kahve Kategorisi 1.5x Star',
    'SpendRule',
    'order.created',
    '[{"field": "items.category", "op": "in", "value": ["coffee"]}]',
    '{"type": "spend", "rate": 0.15}',
    '018fcd02-0000-7000-8000-000000000001',
    NULL,
    30,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 4: Kış Kampanyası — 3x Star
--   stackable: false, priority: 100 (winner-takes-all)
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fcd03-0000-7000-8000-000000000004',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'Kış Kampanyası 3x Star',
    'SpendRule',
    'order.created',
    NULL,
    '{"type": "spend", "rate": 0.30}',
    '018fcd02-0000-7000-8000-000000000001',
    '{"per_customer_total": 3000}',
    100,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 5: İlk Alışveriş Bonusu — +50 Star sabit
--   stackable: true, kişi başı toplam limit: 50 (bir kez)
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fcd03-0000-7000-8000-000000000005',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'İlk Alışveriş +50 Star',
    'FixedBonusRule',
    'order.created',
    NULL,
    '{"type": "fixed", "amount": 50}',
    '018fcd02-0000-7000-8000-000000000001',
    '{"per_customer_total": 50}',
    5,
    true,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 6: Kahve Damgası — kahve kategorisi her alışverişte 1 damga
--   stackable: true
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fcd03-0000-7000-8000-000000000006',
    'starbucks',
    '018fcd01-0000-7000-8000-000000000001',
    'Kahve Damgası',
    'StampRule',
    'order.created',
    '[{"field": "items.category", "op": "in", "value": ["coffee"]}]',
    '{"type": "stamp", "amount": 1}',
    '018fcd02-0000-7000-8000-000000000003',
    NULL,
    10,
    true,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

COMMIT;

-- --------------------------------------------------------
-- Doğrulama
-- --------------------------------------------------------
SELECT 'PROGRAM'      AS entity, id::text, name, status           FROM programs        WHERE tenant_id = 'starbucks'
UNION ALL
SELECT 'ACCOUNT_TYPE', id::text, name, type                       FROM account_types   WHERE tenant_id = 'starbucks'
UNION ALL
SELECT 'TIER',         id::text, name || ' (' || display_name || ')', 'sort=' || sort_order::text FROM tier_definitions WHERE tenant_id = 'starbucks'
UNION ALL
SELECT 'RULE',         id::text, name, status                     FROM rules           WHERE tenant_id = 'starbucks'
ORDER BY entity, name;
