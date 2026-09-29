-- Tenant Provisioning Script
-- Run once per new tenant, replace :tenant_id with the actual tenant identifier (e.g. 'starbucks')
--
-- Usage:
--   psql -d loyalty_dev -v tenant_id=starbucks -f provision_tenant.sql
--
-- Not: Bu script sadece partition oluşturur.
-- Program, account_type, tier_definitions ve rules için
-- tenant'a özgü seed script'i ayrıca çalıştırın.
-- (örnek: scripts/starbucks_loyalty.sql)

-- --------------------------------------------------------
-- 1. Partitions
--    ledger_entries, event_inbox ve event_log LIST partition'dır.
--    Diğer tablolar (programs, account_types, rules,
--    tier_definitions, customer_accounts, reward_log,
--    tier_upgrade_log) partition gerektirmez.
-- --------------------------------------------------------
CREATE TABLE IF NOT EXISTS ledger_entries_:"tenant_id"
    PARTITION OF ledger_entries
    FOR VALUES IN (:'tenant_id');

CREATE TABLE IF NOT EXISTS event_inbox_:"tenant_id"
    PARTITION OF event_inbox
    FOR VALUES IN (:'tenant_id');

CREATE TABLE IF NOT EXISTS event_log_:"tenant_id"
    PARTITION OF event_log
    FOR VALUES IN (:'tenant_id');

-- --------------------------------------------------------
-- 2. Verify
-- --------------------------------------------------------
SELECT
    parent.relname  AS parent_table,
    child.relname   AS partition_table,
    pg_get_expr(child.relpartbound, child.oid) AS bound
FROM pg_inherits
JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
JOIN pg_class child  ON pg_inherits.inhrelid  = child.oid
WHERE parent.relname IN ('ledger_entries', 'event_inbox', 'event_log')
ORDER BY parent.relname, child.relname;
