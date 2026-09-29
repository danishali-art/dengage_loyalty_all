-- ============================================================
-- Burger King Loyalty — Örnek Tenant Seed
-- Tenant ID : burgerking
-- ============================================================
-- Çalıştırma:
--   docker exec -i loyalty-postgres-1 psql -U postgres -d loyalty_dev < scripts/burgerking_loyalty.sql
--
-- Starbucks'tan kasıtlı farklılıklar (multi-tenant test için):
--   - POINTS expiration_days = 180 (Starbucks: 365)
--   - Redemption: 200 crown = 20 TL  (Starbucks: 500 star = 50 TL)
--   - Tier model: Bronz=lifetime (hiç düşmez), Silver=dönemsel 90 gün, Gold=dönemsel 90 gün
--   - Grace: Silver 0 gün (anında düşer), Gold 7 gün
--   - Qualifying account: POINTS (Crown)
-- ============================================================

BEGIN;

-- --------------------------------------------------------
-- 0. Partition
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS ledger_entries_burgerking
    PARTITION OF ledger_entries FOR VALUES IN ('burgerking');

CREATE TABLE IF NOT EXISTS event_inbox_burgerking
    PARTITION OF event_inbox FOR VALUES IN ('burgerking');

-- --------------------------------------------------------
-- 1. Program
-- --------------------------------------------------------
INSERT INTO programs (id, tenant_id, name, status, created_at)
VALUES (
    '018fce01-0000-7000-8000-000000000001',
    'burgerking',
    'BK Crown',
    'active',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- --------------------------------------------------------
-- 2. Account Types
-- --------------------------------------------------------

-- 2a. POINTS — Crown (puan hesabı)
--   expiration_days = 180  (Starbucks'tan farklı: 6 ay)
--   Redemption: 200 crown = 20 TL (rate: 0.10, min: 200)
INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at)
VALUES (
    '018fce02-0000-7000-8000-000000000001',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'POINTS',
    'Crown',
    '{
        "expiration_days": 180,
        "redemption": {
            "target_account_type_id": "018fce02-0000-7000-8000-000000000002",
            "rate": 0.10,
            "min_points": 200
        }
    }',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- 2b. CASH — BK Card
INSERT INTO account_types (id, tenant_id, program_id, type, name, config, created_at)
VALUES (
    '018fce02-0000-7000-8000-000000000002',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'CASH',
    'BK Card',
    '{}',
    NOW()
)
ON CONFLICT (id) DO NOTHING;

-- --------------------------------------------------------
-- 3. Tier Tanımları
--    Kasıtlı farklı model: karma (lifetime + dönemsel)
-- --------------------------------------------------------

-- 3a. Bronz — LIFETIME model (qualifying_days NULL → hiç düşmez)
--     Eşik: 0 puan (herkes buradan başlar)
--     grace_days: 0 (anlamsız, lifetime'da kullanılmaz)
INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '018fce04-0000-7000-8000-000000000001',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'bronz',
    'Bronz Üye',
    0,
    NULL,   -- LIFETIME: dönem yok, hiç düşmez
    0,
    1,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

-- 3b. Silver — Dönemsel 90 gün, grace 0 gün (anında düşer)
--     Eşik: 500 crown
INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '018fce04-0000-7000-8000-000000000002',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'silver',
    'Silver Üye',
    500,
    90,     -- 90 günde bir değerlendirme
    0,      -- grace yok, anında düşer
    2,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

-- 3c. Gold — Dönemsel 90 gün, grace 7 gün
--     Eşik: 2000 crown
INSERT INTO tier_definitions (id, tenant_id, program_id, name, display_name, min_points, qualifying_days, grace_days, sort_order, created_at)
VALUES (
    '018fce04-0000-7000-8000-000000000003',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'gold',
    'Gold Üye',
    2000,
    90,     -- 90 günde bir değerlendirme
    7,      -- 7 gün grace
    3,
    NOW()
)
ON CONFLICT (program_id, name) DO NOTHING;

-- --------------------------------------------------------
-- 4. Program — qualifying_account_type_id bağla
-- --------------------------------------------------------
UPDATE programs
SET qualifying_account_type_id = '018fce02-0000-7000-8000-000000000001'
WHERE id = '018fce01-0000-7000-8000-000000000001'
  AND qualifying_account_type_id IS NULL;

-- --------------------------------------------------------
-- 5. Rules
-- --------------------------------------------------------

-- Rule 1: Temel puan — her 10 TL = 1 Crown
--   stackable: false, priority: 10
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fce03-0000-7000-8000-000000000001',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'Temel Puan — 10 TL = 1 Crown',
    'SpendRule',
    'order.created',
    NULL,
    '{"type": "spend", "rate": 0.10}',
    '018fce02-0000-7000-8000-000000000001',
    NULL,
    10,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 2: Whopper kampanyası — 2x Crown
--   stackable: false, priority: 50
--   Koşul: category = burger
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fce03-0000-7000-8000-000000000002',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'Whopper 2x Crown',
    'SpendRule',
    'order.created',
    '[{"field": "items.category", "op": "in", "value": ["burger"]}]',
    '{"type": "spend", "rate": 0.20}',
    '018fce02-0000-7000-8000-000000000001',
    NULL,
    50,
    false,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 3: Silver üye bonusu — +20 Crown sabit (her siparişte)
--   stackable: true, tier koşulu: silver
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fce03-0000-7000-8000-000000000003',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'Silver Üye +20 Crown',
    'FixedBonusRule',
    'order.created',
    '[{"field": "tier", "op": "in", "value": ["silver"]}]',
    '{"type": "fixed", "amount": 20}',
    '018fce02-0000-7000-8000-000000000001',
    NULL,
    5,
    true,
    NULL, NULL,
    'active',
    NOW(), NOW()
)
ON CONFLICT (id) DO NOTHING;

-- Rule 4: Gold üye bonusu — +50 Crown sabit (her siparişte)
--   stackable: true, tier koşulu: gold
INSERT INTO rules (
    id, tenant_id, program_id, name, type, trigger,
    conditions, calculation, target_account_type_id,
    limits, priority, stackable, active_from, active_to, status,
    created_at, updated_at
) VALUES (
    '018fce03-0000-7000-8000-000000000004',
    'burgerking',
    '018fce01-0000-7000-8000-000000000001',
    'Gold Üye +50 Crown',
    'FixedBonusRule',
    'order.created',
    '[{"field": "tier", "op": "in", "value": ["gold"]}]',
    '{"type": "fixed", "amount": 50}',
    '018fce02-0000-7000-8000-000000000001',
    NULL,
    5,
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
SELECT 'PROGRAM'      AS entity, id::text, name, status                                      FROM programs        WHERE tenant_id = 'burgerking'
UNION ALL
SELECT 'ACCOUNT_TYPE', id::text, name, type                                                  FROM account_types   WHERE tenant_id = 'burgerking'
UNION ALL
SELECT 'TIER',         id::text, name || ' (' || display_name || ')',
       'sort=' || sort_order::text || ' min=' || min_points::text ||
       CASE WHEN qualifying_days IS NULL THEN ' LIFETIME' ELSE ' q=' || qualifying_days::text || 'd grace=' || grace_days::text || 'd' END
FROM tier_definitions WHERE tenant_id = 'burgerking'
UNION ALL
SELECT 'RULE',         id::text, name, status                                                FROM rules           WHERE tenant_id = 'burgerking'
ORDER BY entity, name;
