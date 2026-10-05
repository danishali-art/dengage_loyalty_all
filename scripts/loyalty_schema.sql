CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE TABLE programs (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        name character varying(255) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_programs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE TABLE account_types (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        program_id uuid NOT NULL,
        type character varying(20) NOT NULL,
        name character varying(255) NOT NULL,
        config jsonb NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_account_types" PRIMARY KEY (id),
        CONSTRAINT "FK_account_types_programs_program_id" FOREIGN KEY (program_id) REFERENCES programs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE TABLE customer_accounts (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        contact_key character varying(255) NOT NULL,
        account_type_id uuid NOT NULL,
        balance numeric(20,4) NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_customer_accounts" PRIMARY KEY (id),
        CONSTRAINT "FK_customer_accounts_account_types_account_type_id" FOREIGN KEY (account_type_id) REFERENCES account_types (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE TABLE rules (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        program_id uuid NOT NULL,
        name character varying(255) NOT NULL,
        type character varying(50) NOT NULL,
        trigger character varying(50) NOT NULL,
        conditions jsonb,
        calculation jsonb NOT NULL,
        target_account_type_id uuid NOT NULL,
        limits jsonb,
        priority integer NOT NULL,
        stackable boolean NOT NULL,
        active_from date,
        active_to date,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_rules" PRIMARY KEY (id),
        CONSTRAINT "FK_rules_account_types_target_account_type_id" FOREIGN KEY (target_account_type_id) REFERENCES account_types (id) ON DELETE CASCADE,
        CONSTRAINT "FK_rules_programs_program_id" FOREIGN KEY (program_id) REFERENCES programs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE TABLE reward_log (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        contact_key character varying(255) NOT NULL,
        account_type_id uuid NOT NULL,
        reward_type character varying(100) NOT NULL,
        source_event_id character varying(255) NOT NULL,
        ledger_reset_entry_id uuid NOT NULL,
        completion_count integer NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        delivered_at timestamp with time zone,
        CONSTRAINT "PK_reward_log" PRIMARY KEY (id),
        CONSTRAINT "FK_reward_log_account_types_account_type_id" FOREIGN KEY (account_type_id) REFERENCES account_types (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN

    CREATE TABLE ledger_entries (
        id                  uuid                        NOT NULL,
        tenant_id           character varying(100)      NOT NULL,
        customer_account_id uuid                        NOT NULL,
        contact_key         character varying(255)      NOT NULL,
        delta               numeric(20,4)               NOT NULL,
        reason              character varying(50)       NOT NULL,
        source_event_id     character varying(255)      NOT NULL,
        rule_id             uuid,
        idempotency_key     character varying(500)      NOT NULL,
        metadata            jsonb,
        created_at          timestamp with time zone    NOT NULL
    ) PARTITION BY LIST (tenant_id);

    CREATE UNIQUE INDEX uq_ledger_entries_idempotency_key
        ON ledger_entries (tenant_id, idempotency_key);
    CREATE INDEX idx_ledger_entries_tenant_account_date
        ON ledger_entries (tenant_id, customer_account_id, created_at);
    CREATE INDEX idx_ledger_entries_tenant_source_event
        ON ledger_entries (tenant_id, source_event_id);
    CREATE INDEX "IX_ledger_entries_customer_account_id"
        ON ledger_entries (customer_account_id);
    CREATE INDEX "IX_ledger_entries_rule_id"
        ON ledger_entries (rule_id);

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN

    CREATE TABLE event_inbox (
        event_id        character varying(255)      NOT NULL,
        tenant_id       character varying(100)      NOT NULL,
        event_type      character varying(50)       NOT NULL,
        payload         jsonb                       NOT NULL,
        received_at     timestamp with time zone    NOT NULL,
        processed_at    timestamp with time zone,
        status          character varying(20)       NOT NULL,
        error           text,
        CONSTRAINT "PK_event_inbox" PRIMARY KEY (tenant_id, event_id)
    ) PARTITION BY LIST (tenant_id);

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX idx_account_types_tenant_program ON account_types (tenant_id, program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX "IX_account_types_program_id" ON account_types (program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX idx_customer_accounts_tenant_contact ON customer_accounts (tenant_id, contact_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX "IX_customer_accounts_account_type_id" ON customer_accounts (account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE UNIQUE INDEX uq_customer_accounts_tenant_contact_accounttype ON customer_accounts (tenant_id, contact_key, account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX idx_programs_tenant_id ON programs (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX idx_reward_log_tenant_contact_status ON reward_log (tenant_id, contact_key, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX "IX_reward_log_account_type_id" ON reward_log (account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX idx_rules_tenant_program_status ON rules (tenant_id, program_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX "IX_rules_program_id" ON rules (program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    CREATE INDEX "IX_rules_target_account_type_id" ON rules (target_account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260630013228_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260630013228_InitialCreate', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    ALTER TABLE programs ADD qualifying_account_type_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    ALTER TABLE customer_accounts ADD tier_expires_at date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    ALTER TABLE customer_accounts ADD tier_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    ALTER TABLE customer_accounts ADD tier_period_start date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    ALTER TABLE customer_accounts ADD tier_qualifying_pts numeric(20,4) NOT NULL DEFAULT 0.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE TABLE tier_definitions (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        program_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        display_name character varying(255) NOT NULL,
        min_points numeric(20,4) NOT NULL,
        qualifying_days integer,
        grace_days integer NOT NULL DEFAULT 0,
        sort_order integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_tier_definitions" PRIMARY KEY (id),
        CONSTRAINT "FK_tier_definitions_programs_program_id" FOREIGN KEY (program_id) REFERENCES programs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE TABLE tier_upgrade_log (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        contact_key character varying(255) NOT NULL,
        from_tier_id uuid,
        to_tier_id uuid NOT NULL,
        qualifying_pts numeric(20,4) NOT NULL,
        source_event_id character varying(255) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_tier_upgrade_log" PRIMARY KEY (id),
        CONSTRAINT "FK_tier_upgrade_log_tier_definitions_from_tier_id" FOREIGN KEY (from_tier_id) REFERENCES tier_definitions (id),
        CONSTRAINT "FK_tier_upgrade_log_tier_definitions_to_tier_id" FOREIGN KEY (to_tier_id) REFERENCES tier_definitions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE INDEX "IX_programs_qualifying_account_type_id" ON programs (qualifying_account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE INDEX "IX_customer_accounts_tier_id" ON customer_accounts (tier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE INDEX idx_tier_definitions_tenant_program ON tier_definitions (tenant_id, program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE UNIQUE INDEX uq_tier_definitions_program_name ON tier_definitions (program_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE INDEX idx_tier_upgrade_log_tenant_contact ON tier_upgrade_log (tenant_id, contact_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE INDEX "IX_tier_upgrade_log_from_tier_id" ON tier_upgrade_log (from_tier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    CREATE INDEX "IX_tier_upgrade_log_to_tier_id" ON tier_upgrade_log (to_tier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    ALTER TABLE customer_accounts ADD CONSTRAINT "FK_customer_accounts_tier_definitions_tier_id" FOREIGN KEY (tier_id) REFERENCES tier_definitions (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    ALTER TABLE programs ADD CONSTRAINT "FK_programs_account_types_qualifying_account_type_id" FOREIGN KEY (qualifying_account_type_id) REFERENCES account_types (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701184514_TierSystem') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260701184514_TierSystem', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701210128_TierUpgradeLogSourceEventUnique') THEN
    CREATE UNIQUE INDEX ux_tier_upgrade_log_source_event ON tier_upgrade_log (tenant_id, contact_key, source_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260701210128_TierUpgradeLogSourceEventUnique') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260701210128_TierUpgradeLogSourceEventUnique', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260702024651_RulesSoftDeleteTrigger') THEN
    CREATE OR REPLACE FUNCTION rules_soft_delete_trg()
    RETURNS TRIGGER
    LANGUAGE plpgsql
    AS $$
    BEGIN
        IF OLD.status = 'deleted' THEN
            RETURN OLD;  -- purge: actually delete
        END IF;

        UPDATE rules
           SET status     = 'deleted',
               updated_at = NOW()
         WHERE id = OLD.id;

        RETURN NULL;  -- cancel the physical DELETE
    END;
    $$;

    DROP TRIGGER IF EXISTS trg_rules_soft_delete ON rules;
    CREATE TRIGGER trg_rules_soft_delete
        BEFORE DELETE ON rules
        FOR EACH ROW
        EXECUTE FUNCTION rules_soft_delete_trg();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260702024651_RulesSoftDeleteTrigger') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260702024651_RulesSoftDeleteTrigger', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705202945_RewardLogSourceEventUnique') THEN
    CREATE UNIQUE INDEX ux_reward_log_source_event ON reward_log (tenant_id, account_type_id, source_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705202945_RewardLogSourceEventUnique') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260705202945_RewardLogSourceEventUnique', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    ALTER TABLE reward_log ADD reward_definition_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    CREATE TABLE reward_definitions (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        program_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        display_name character varying(255) NOT NULL,
        acquisition character varying(30) NOT NULL,
        stamp_account_type_id uuid,
        points_price numeric(20,4),
        points_account_type_id uuid,
        external_coupon_type character varying(255) NOT NULL,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_reward_definitions" PRIMARY KEY (id),
        CONSTRAINT "FK_reward_definitions_account_types_points_account_type_id" FOREIGN KEY (points_account_type_id) REFERENCES account_types (id),
        CONSTRAINT "FK_reward_definitions_account_types_stamp_account_type_id" FOREIGN KEY (stamp_account_type_id) REFERENCES account_types (id),
        CONSTRAINT "FK_reward_definitions_programs_program_id" FOREIGN KEY (program_id) REFERENCES programs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    CREATE INDEX "IX_reward_log_reward_definition_id" ON reward_log (reward_definition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    CREATE INDEX idx_reward_definitions_tenant_program ON reward_definitions (tenant_id, program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    CREATE INDEX "IX_reward_definitions_points_account_type_id" ON reward_definitions (points_account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    CREATE INDEX "IX_reward_definitions_program_id" ON reward_definitions (program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    CREATE INDEX "IX_reward_definitions_stamp_account_type_id" ON reward_definitions (stamp_account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    CREATE UNIQUE INDEX uq_reward_definitions_tenant_program_name ON reward_definitions (tenant_id, program_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    CREATE UNIQUE INDEX ux_reward_definitions_active_stamp ON reward_definitions (tenant_id, stamp_account_type_id) WHERE acquisition = 'stamp_completion' AND is_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    ALTER TABLE reward_log ADD CONSTRAINT "FK_reward_log_reward_definitions_reward_definition_id" FOREIGN KEY (reward_definition_id) REFERENCES reward_definitions (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260705235304_RewardDefinitions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260705235304_RewardDefinitions', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260706003307_OutboxEvents') THEN
    CREATE TABLE outbox_events (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        event_id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        event_type character varying(100) NOT NULL,
        contact_key character varying(255) NOT NULL,
        payload jsonb NOT NULL,
        dedup_key character varying(255),
        status character varying(20) NOT NULL,
        attempts integer NOT NULL,
        next_attempt_at timestamp with time zone NOT NULL,
        published_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_outbox_events" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260706003307_OutboxEvents') THEN
    CREATE INDEX idx_outbox_pending_next_attempt ON outbox_events (next_attempt_at) WHERE status = 'pending';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260706003307_OutboxEvents') THEN
    CREATE UNIQUE INDEX ux_outbox_dedup ON outbox_events (tenant_id, dedup_key) WHERE dedup_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260706003307_OutboxEvents') THEN
    CREATE UNIQUE INDEX ux_outbox_event_id ON outbox_events (event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260706003307_OutboxEvents') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260706003307_OutboxEvents', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260706070750_ProgramWarningDays') THEN
    ALTER TABLE programs ADD warning_days integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260706070750_ProgramWarningDays') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260706070750_ProgramWarningDays', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829230559_EventLog') THEN

    CREATE TABLE event_log (
        tenant_id       character varying(100)      NOT NULL,
        event_id        character varying(255)      NOT NULL,
        contact_key     character varying(255)      NOT NULL,
        event_type      character varying(50)       NOT NULL,
        occurred_at     timestamp with time zone    NOT NULL,
        CONSTRAINT "PK_event_log" PRIMARY KEY (tenant_id, event_id)
    ) PARTITION BY LIST (tenant_id);

    CREATE INDEX idx_event_log_contact_type_time
        ON event_log (tenant_id, contact_key, event_type, occurred_at DESC);
    CREATE INDEX idx_event_log_occurred
        ON event_log (occurred_at);

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829230559_EventLog') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260829230559_EventLog', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829232413_RuleConditionsDsl') THEN
    UPDATE rules SET conditions =
        COALESCE(CASE WHEN conditions ? 'channel' THEN
            jsonb_build_array(jsonb_build_object('field', 'channel', 'op', 'eq', 'value', conditions->'channel')) END, '[]'::jsonb)
     || COALESCE(CASE WHEN conditions ? 'payment_method' THEN
            jsonb_build_array(jsonb_build_object('field', 'payment_method', 'op', 'eq', 'value', conditions->'payment_method')) END, '[]'::jsonb)
     || COALESCE(CASE WHEN conditions #> '{amount,gte}' IS NOT NULL THEN
            jsonb_build_array(jsonb_build_object('field', 'amount', 'op', 'gte', 'value', conditions#>'{amount,gte}')) END, '[]'::jsonb)
     || COALESCE(CASE WHEN conditions #> '{amount,lte}' IS NOT NULL THEN
            jsonb_build_array(jsonb_build_object('field', 'amount', 'op', 'lte', 'value', conditions#>'{amount,lte}')) END, '[]'::jsonb)
     || COALESCE(CASE WHEN conditions #> '{amount,gt}' IS NOT NULL THEN
            jsonb_build_array(jsonb_build_object('field', 'amount', 'op', 'gt', 'value', conditions#>'{amount,gt}')) END, '[]'::jsonb)
     || COALESCE(CASE WHEN conditions #> '{amount,lt}' IS NOT NULL THEN
            jsonb_build_array(jsonb_build_object('field', 'amount', 'op', 'lt', 'value', conditions#>'{amount,lt}')) END, '[]'::jsonb)
     || COALESCE(CASE WHEN conditions #> '{item_category,in}' IS NOT NULL THEN
            jsonb_build_array(jsonb_build_object('field', 'items.category', 'op', 'in', 'value', conditions#>'{item_category,in}')) END, '[]'::jsonb)
     || COALESCE(CASE WHEN conditions #> '{tier,in}' IS NOT NULL THEN
            jsonb_build_array(jsonb_build_object('field', 'tier', 'op', 'in', 'value', conditions#>'{tier,in}')) END, '[]'::jsonb)
    WHERE conditions IS NOT NULL AND jsonb_typeof(conditions) = 'object';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829232413_RuleConditionsDsl') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260829232413_RuleConditionsDsl', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829232444_RuleActiveWindowTimestamptz') THEN
    ALTER TABLE rules
        ALTER COLUMN active_from TYPE timestamp with time zone
            USING (active_from::timestamp AT TIME ZONE 'UTC'),
        ALTER COLUMN active_to TYPE timestamp with time zone
            USING ((active_to + 1)::timestamp AT TIME ZONE 'UTC');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829232444_RuleActiveWindowTimestamptz') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260829232444_RuleActiveWindowTimestamptz', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829235815_Streak') THEN
    ALTER TABLE rules ADD streak_config jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829235815_Streak') THEN
    CREATE TABLE streak_applied_event (
        tenant_id character varying(100) NOT NULL,
        rule_id uuid NOT NULL,
        event_id character varying(255) NOT NULL,
        applied_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_streak_applied_event" PRIMARY KEY (tenant_id, rule_id, event_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829235815_Streak') THEN
    CREATE TABLE streak_log (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        rule_id uuid NOT NULL,
        contact_key character varying(255) NOT NULL,
        completion_no integer NOT NULL,
        completed_period date NOT NULL,
        periods integer NOT NULL,
        reward_kind character varying(50) NOT NULL,
        reward_ref character varying(255),
        source_event_id character varying(255) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_streak_log" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829235815_Streak') THEN
    CREATE TABLE streak_period_state (
        tenant_id character varying(100) NOT NULL,
        rule_id uuid NOT NULL,
        contact_key character varying(255) NOT NULL,
        period_start date NOT NULL,
        agg_sum numeric(18,2) NOT NULL,
        agg_count integer NOT NULL,
        met boolean NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_streak_period_state" PRIMARY KEY (tenant_id, rule_id, contact_key, period_start)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829235815_Streak') THEN
    CREATE TABLE streak_progress (
        tenant_id character varying(100) NOT NULL,
        rule_id uuid NOT NULL,
        contact_key character varying(255) NOT NULL,
        streak_count integer NOT NULL,
        last_met_period date,
        completions integer NOT NULL,
        status character varying(20) NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_streak_progress" PRIMARY KEY (tenant_id, rule_id, contact_key)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829235815_Streak') THEN
    CREATE UNIQUE INDEX ux_streak_log_completion ON streak_log (tenant_id, rule_id, contact_key, completion_no);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829235815_Streak') THEN
    CREATE INDEX idx_streak_period_state_rule_period ON streak_period_state (tenant_id, rule_id, period_start);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260829235815_Streak') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260829235815_Streak', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    ALTER TABLE programs ADD description text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    CREATE TABLE tenants (
        id character varying(50) NOT NULL,
        name character varying(255) NOT NULL,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_tenants" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    CREATE TABLE admin_users (
        id uuid NOT NULL,
        email character varying(255) NOT NULL,
        password_hash character varying(500) NOT NULL,
        role character varying(30) NOT NULL,
        tenant_id character varying(50),
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_admin_users" PRIMARY KEY (id),
        CONSTRAINT "FK_admin_users_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    CREATE TABLE tenant_api_keys (
        id uuid NOT NULL,
        tenant_id character varying(50) NOT NULL,
        key_prefix character varying(20) NOT NULL,
        hashed_key character varying(255) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        last_used_at timestamp with time zone,
        revoked_at timestamp with time zone,
        CONSTRAINT "PK_tenant_api_keys" PRIMARY KEY (id),
        CONSTRAINT "FK_tenant_api_keys_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    CREATE UNIQUE INDEX uq_tier_definitions_program_sort_order ON tier_definitions (program_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    CREATE INDEX idx_admin_users_tenant_id ON admin_users (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    CREATE UNIQUE INDEX uq_admin_users_email ON admin_users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    CREATE INDEX idx_tenant_api_keys_tenant_id ON tenant_api_keys (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    CREATE UNIQUE INDEX uq_tenant_api_keys_prefix ON tenant_api_keys (key_prefix);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902122303_MultitenantAdminSchema') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260902122303_MultitenantAdminSchema', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902171315_WidenApiKeyPrefixColumn') THEN
    ALTER TABLE tenant_api_keys ALTER COLUMN key_prefix TYPE character varying(80);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260902171315_WidenApiKeyPrefixColumn') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260902171315_WidenApiKeyPrefixColumn', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903122313_AddRuleFireAudit') THEN
    CREATE TABLE rule_fire_audit (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        rule_id uuid NOT NULL,
        rule_version timestamp with time zone NOT NULL,
        source_event_id character varying(255) NOT NULL,
        contact_key character varying(255) NOT NULL,
        conditions_snapshot jsonb,
        calculation_snapshot jsonb NOT NULL,
        resulting_delta numeric(20,4) NOT NULL,
        ledger_entry_id uuid,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_rule_fire_audit" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903122313_AddRuleFireAudit') THEN
    CREATE INDEX idx_rule_fire_audit_tenant_rule_date ON rule_fire_audit (tenant_id, rule_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903122313_AddRuleFireAudit') THEN
    CREATE UNIQUE INDEX ux_rule_fire_audit_source_event_rule ON rule_fire_audit (tenant_id, source_event_id, rule_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903122313_AddRuleFireAudit') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260903122313_AddRuleFireAudit', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    ALTER TABLE streak_progress RENAME COLUMN rule_id TO campaign_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    ALTER TABLE streak_period_state RENAME COLUMN rule_id TO campaign_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    ALTER TABLE streak_log RENAME COLUMN rule_id TO campaign_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    ALTER TABLE streak_applied_event RENAME COLUMN rule_id TO campaign_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    CREATE TABLE streak_campaigns (
        id uuid NOT NULL,
        tenant_id character varying(100) NOT NULL,
        program_id uuid NOT NULL,
        name character varying(255) NOT NULL,
        trigger character varying(50) NOT NULL,
        target_account_type_id uuid NOT NULL,
        conditions jsonb,
        config jsonb NOT NULL,
        active_from timestamp with time zone,
        active_to timestamp with time zone,
        status character varying(20) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_streak_campaigns" PRIMARY KEY (id),
        CONSTRAINT "FK_streak_campaigns_account_types_target_account_type_id" FOREIGN KEY (target_account_type_id) REFERENCES account_types (id) ON DELETE CASCADE,
        CONSTRAINT "FK_streak_campaigns_programs_program_id" FOREIGN KEY (program_id) REFERENCES programs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    CREATE INDEX idx_streak_campaigns_tenant_program_status ON streak_campaigns (tenant_id, program_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    CREATE INDEX "IX_streak_campaigns_program_id" ON streak_campaigns (program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    CREATE INDEX "IX_streak_campaigns_target_account_type_id" ON streak_campaigns (target_account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    INSERT INTO streak_campaigns (
        id, tenant_id, program_id, name, trigger, target_account_type_id,
        conditions, config, active_from, active_to, status, created_at, updated_at
    )
    SELECT
        id, tenant_id, program_id, name, trigger, target_account_type_id,
        conditions, streak_config, active_from, active_to, status, created_at, updated_at
    FROM rules
    WHERE type = 'StreakRule';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    UPDATE rules SET status = 'deleted', updated_at = NOW() WHERE type = 'StreakRule' AND status <> 'deleted';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    DELETE FROM rules WHERE type = 'StreakRule';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    ALTER TABLE rules DROP COLUMN streak_config;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903170930_AddStreakCampaigns') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260903170930_AddStreakCampaigns', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903184551_AddRuleTemplate') THEN
    ALTER TABLE rules ADD template character varying(50);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903184551_AddRuleTemplate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260903184551_AddRuleTemplate', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE admin_users DROP CONSTRAINT "FK_admin_users_tenants_tenant_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenant_api_keys DROP CONSTRAINT "FK_tenant_api_keys_tenants_tenant_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    DROP INDEX idx_admin_users_tenant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    DROP INDEX idx_tenant_api_keys_tenant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenants DROP CONSTRAINT "PK_tenants";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenants RENAME COLUMN id TO slug;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenants ADD id uuid NOT NULL DEFAULT (gen_random_uuid());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenants ALTER COLUMN id DROP DEFAULT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenants ADD CONSTRAINT "PK_tenants" PRIMARY KEY (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    CREATE UNIQUE INDEX ix_tenants_slug ON tenants (slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE admin_users ADD tenant_id_new uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    UPDATE admin_users a SET tenant_id_new = t.id
    FROM tenants t WHERE t.slug = a.tenant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE admin_users DROP COLUMN tenant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE admin_users RENAME COLUMN tenant_id_new TO tenant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    CREATE INDEX idx_admin_users_tenant_id ON admin_users (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE admin_users ADD CONSTRAINT "FK_admin_users_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenant_api_keys ADD tenant_id_new uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    UPDATE tenant_api_keys k SET tenant_id_new = t.id
    FROM tenants t WHERE t.slug = k.tenant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenant_api_keys ALTER COLUMN tenant_id_new SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenant_api_keys DROP COLUMN tenant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenant_api_keys RENAME COLUMN tenant_id_new TO tenant_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    CREATE INDEX idx_tenant_api_keys_tenant_id ON tenant_api_keys (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    ALTER TABLE tenant_api_keys ADD CONSTRAINT "FK_tenant_api_keys_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903193446_TenantGuidSurrogateKey') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260903193446_TenantGuidSurrogateKey', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE account_types ADD COLUMN tenant_id_new uuid;
    UPDATE account_types t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE account_types ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE account_types DROP COLUMN tenant_id;
    ALTER TABLE account_types RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE account_types ADD CONSTRAINT "FK_account_types_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE customer_accounts ADD COLUMN tenant_id_new uuid;
    UPDATE customer_accounts t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE customer_accounts ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE customer_accounts DROP COLUMN tenant_id;
    ALTER TABLE customer_accounts RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE customer_accounts ADD CONSTRAINT "FK_customer_accounts_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE outbox_events ADD COLUMN tenant_id_new uuid;
    UPDATE outbox_events t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE outbox_events ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE outbox_events DROP COLUMN tenant_id;
    ALTER TABLE outbox_events RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE outbox_events ADD CONSTRAINT "FK_outbox_events_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE programs ADD COLUMN tenant_id_new uuid;
    UPDATE programs t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE programs ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE programs DROP COLUMN tenant_id;
    ALTER TABLE programs RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE programs ADD CONSTRAINT "FK_programs_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE reward_definitions ADD COLUMN tenant_id_new uuid;
    UPDATE reward_definitions t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE reward_definitions ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE reward_definitions DROP COLUMN tenant_id;
    ALTER TABLE reward_definitions RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE reward_definitions ADD CONSTRAINT "FK_reward_definitions_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE reward_log ADD COLUMN tenant_id_new uuid;
    UPDATE reward_log t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE reward_log ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE reward_log DROP COLUMN tenant_id;
    ALTER TABLE reward_log RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE reward_log ADD CONSTRAINT "FK_reward_log_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE rules ADD COLUMN tenant_id_new uuid;
    UPDATE rules t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE rules ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE rules DROP COLUMN tenant_id;
    ALTER TABLE rules RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE rules ADD CONSTRAINT "FK_rules_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE rule_fire_audit ADD COLUMN tenant_id_new uuid;
    UPDATE rule_fire_audit t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE rule_fire_audit ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE rule_fire_audit DROP COLUMN tenant_id;
    ALTER TABLE rule_fire_audit RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE rule_fire_audit ADD CONSTRAINT "FK_rule_fire_audit_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE streak_campaigns ADD COLUMN tenant_id_new uuid;
    UPDATE streak_campaigns t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE streak_campaigns ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE streak_campaigns DROP COLUMN tenant_id;
    ALTER TABLE streak_campaigns RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE streak_campaigns ADD CONSTRAINT "FK_streak_campaigns_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE streak_log ADD COLUMN tenant_id_new uuid;
    UPDATE streak_log t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE streak_log ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE streak_log DROP COLUMN tenant_id;
    ALTER TABLE streak_log RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE streak_log ADD CONSTRAINT "FK_streak_log_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE tier_definitions ADD COLUMN tenant_id_new uuid;
    UPDATE tier_definitions t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE tier_definitions ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE tier_definitions DROP COLUMN tenant_id;
    ALTER TABLE tier_definitions RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE tier_definitions ADD CONSTRAINT "FK_tier_definitions_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE tier_upgrade_log ADD COLUMN tenant_id_new uuid;
    UPDATE tier_upgrade_log t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE tier_upgrade_log ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE tier_upgrade_log DROP COLUMN tenant_id;
    ALTER TABLE tier_upgrade_log RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE tier_upgrade_log ADD CONSTRAINT "FK_tier_upgrade_log_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE streak_applied_event DROP CONSTRAINT "PK_streak_applied_event";
    ALTER TABLE streak_applied_event ADD COLUMN tenant_id_new uuid;
    UPDATE streak_applied_event t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE streak_applied_event ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE streak_applied_event DROP COLUMN tenant_id;
    ALTER TABLE streak_applied_event RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE streak_applied_event ADD CONSTRAINT "PK_streak_applied_event" PRIMARY KEY (tenant_id, campaign_id, event_id);
    ALTER TABLE streak_applied_event ADD CONSTRAINT "FK_streak_applied_event_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE streak_period_state DROP CONSTRAINT "PK_streak_period_state";
    ALTER TABLE streak_period_state ADD COLUMN tenant_id_new uuid;
    UPDATE streak_period_state t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE streak_period_state ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE streak_period_state DROP COLUMN tenant_id;
    ALTER TABLE streak_period_state RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE streak_period_state ADD CONSTRAINT "PK_streak_period_state" PRIMARY KEY (tenant_id, campaign_id, contact_key, period_start);
    ALTER TABLE streak_period_state ADD CONSTRAINT "FK_streak_period_state_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    ALTER TABLE streak_progress DROP CONSTRAINT "PK_streak_progress";
    ALTER TABLE streak_progress ADD COLUMN tenant_id_new uuid;
    UPDATE streak_progress t SET tenant_id_new = tn.id FROM tenants tn WHERE tn.slug = t.tenant_id;
    ALTER TABLE streak_progress ALTER COLUMN tenant_id_new SET NOT NULL;
    ALTER TABLE streak_progress DROP COLUMN tenant_id;
    ALTER TABLE streak_progress RENAME COLUMN tenant_id_new TO tenant_id;
    ALTER TABLE streak_progress ADD CONSTRAINT "PK_streak_progress" PRIMARY KEY (tenant_id, campaign_id, contact_key);
    ALTER TABLE streak_progress ADD CONSTRAINT "FK_streak_progress_tenants_tenant_id"
        FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903200505_TenantIdGuidForeignKeys') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260903200505_TenantIdGuidForeignKeys', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_account_types_tenant_program ON account_types (tenant_id, program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE UNIQUE INDEX uq_customer_accounts_tenant_contact_accounttype ON customer_accounts (tenant_id, contact_key, account_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_customer_accounts_tenant_contact ON customer_accounts (tenant_id, contact_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE UNIQUE INDEX ux_outbox_dedup ON outbox_events (tenant_id, dedup_key) WHERE dedup_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_programs_tenant_id ON programs (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_reward_definitions_tenant_program ON reward_definitions (tenant_id, program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE UNIQUE INDEX uq_reward_definitions_tenant_program_name ON reward_definitions (tenant_id, program_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE UNIQUE INDEX ux_reward_definitions_active_stamp ON reward_definitions (tenant_id, stamp_account_type_id) WHERE acquisition = 'stamp_completion' AND is_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_reward_log_tenant_contact_status ON reward_log (tenant_id, contact_key, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE UNIQUE INDEX ux_reward_log_source_event ON reward_log (tenant_id, account_type_id, source_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_rules_tenant_program_status ON rules (tenant_id, program_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_rule_fire_audit_tenant_rule_date ON rule_fire_audit (tenant_id, rule_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE UNIQUE INDEX ux_rule_fire_audit_source_event_rule ON rule_fire_audit (tenant_id, source_event_id, rule_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_streak_campaigns_tenant_program_status ON streak_campaigns (tenant_id, program_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE UNIQUE INDEX ux_streak_log_completion ON streak_log (tenant_id, campaign_id, contact_key, completion_no);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_streak_period_state_rule_period ON streak_period_state (tenant_id, campaign_id, period_start);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_tier_definitions_tenant_program ON tier_definitions (tenant_id, program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE INDEX idx_tier_upgrade_log_tenant_contact ON tier_upgrade_log (tenant_id, contact_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    CREATE UNIQUE INDEX ux_tier_upgrade_log_source_event ON tier_upgrade_log (tenant_id, contact_key, source_event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260903201852_RestoreTenantScopedIndexes') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260903201852_RestoreTenantScopedIndexes', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    DROP INDEX uq_tier_definitions_program_name;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    DROP INDEX uq_tier_definitions_program_sort_order;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    ALTER TABLE tier_definitions ADD status character varying(20) NOT NULL DEFAULT 'active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE TABLE complaints (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        program_id uuid,
        customer_key character varying(255),
        subject character varying(255) NOT NULL,
        description text,
        status character varying(20) NOT NULL DEFAULT 'open',
        created_at timestamp with time zone NOT NULL,
        resolved_at timestamp with time zone,
        CONSTRAINT "PK_complaints" PRIMARY KEY (id),
        CONSTRAINT "FK_complaints_programs_program_id" FOREIGN KEY (program_id) REFERENCES programs (id),
        CONSTRAINT "FK_complaints_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE TABLE config_versions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        entity_type character varying(64) NOT NULL,
        entity_id uuid NOT NULL,
        version_number integer NOT NULL,
        snapshot jsonb NOT NULL,
        change_type character varying(20) NOT NULL,
        change_summary text,
        changed_by character varying(255) NOT NULL,
        changed_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_config_versions" PRIMARY KEY (id),
        CONSTRAINT "FK_config_versions_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE UNIQUE INDEX uq_tier_definitions_program_name ON tier_definitions (program_id, name) WHERE status != 'deleted';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE UNIQUE INDEX uq_tier_definitions_program_sort_order ON tier_definitions (program_id, sort_order) WHERE status != 'deleted';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE INDEX idx_complaints_tenant_program ON complaints (tenant_id, program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE INDEX idx_complaints_tenant_status ON complaints (tenant_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE INDEX "IX_complaints_program_id" ON complaints (program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE INDEX idx_config_versions_entity_timeline ON config_versions (tenant_id, entity_type, entity_id, changed_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    CREATE UNIQUE INDEX ux_config_versions_entity_version ON config_versions (tenant_id, entity_type, entity_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184214_AddConfigVersions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260905184214_AddConfigVersions', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184227_AddComplaints') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260905184227_AddComplaints', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184233_AddTierStatus') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260905184233_AddTierStatus', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184245_AddProgramsSoftDeleteTrigger') THEN
    CREATE OR REPLACE FUNCTION programs_soft_delete_trg()
    RETURNS TRIGGER
    LANGUAGE plpgsql
    AS $$
    BEGIN
        IF OLD.status = 'deleted' THEN
            RETURN OLD;  -- purge: actually delete
        END IF;

        UPDATE programs
           SET status = 'deleted'
         WHERE id = OLD.id;

        RETURN NULL;  -- cancel the physical DELETE
    END;
    $$;

    DROP TRIGGER IF EXISTS trg_programs_soft_delete ON programs;
    CREATE TRIGGER trg_programs_soft_delete
        BEFORE DELETE ON programs
        FOR EACH ROW
        EXECUTE FUNCTION programs_soft_delete_trg();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184245_AddProgramsSoftDeleteTrigger') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260905184245_AddProgramsSoftDeleteTrigger', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184251_AddTiersSoftDeleteTrigger') THEN
    CREATE OR REPLACE FUNCTION tier_definitions_soft_delete_trg()
    RETURNS TRIGGER
    LANGUAGE plpgsql
    AS $$
    BEGIN
        IF OLD.status = 'deleted' THEN
            RETURN OLD;  -- purge: actually delete
        END IF;

        UPDATE tier_definitions
           SET status = 'deleted'
         WHERE id = OLD.id;

        RETURN NULL;  -- cancel the physical DELETE
    END;
    $$;

    DROP TRIGGER IF EXISTS trg_tier_definitions_soft_delete ON tier_definitions;
    CREATE TRIGGER trg_tier_definitions_soft_delete
        BEFORE DELETE ON tier_definitions
        FOR EACH ROW
        EXECUTE FUNCTION tier_definitions_soft_delete_trg();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260905184251_AddTiersSoftDeleteTrigger') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260905184251_AddTiersSoftDeleteTrigger', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917103823_RewardTypeTaxonomy') THEN
    ALTER TABLE reward_definitions DROP COLUMN external_coupon_type;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917103823_RewardTypeTaxonomy') THEN
    ALTER TABLE reward_log RENAME COLUMN reward_type TO reward_name;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917103823_RewardTypeTaxonomy') THEN
    ALTER TABLE reward_definitions ADD reward_type character varying(30) NOT NULL DEFAULT 'points_bonus';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917103823_RewardTypeTaxonomy') THEN
    ALTER TABLE reward_definitions ADD type_config jsonb NOT NULL DEFAULT '{}';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260917103823_RewardTypeTaxonomy') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260917103823_RewardTypeTaxonomy', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921102134_RuleTypeExpansionCr02') THEN
    ALTER TABLE rules DROP CONSTRAINT "FK_rules_account_types_target_account_type_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921102134_RuleTypeExpansionCr02') THEN
    ALTER TABLE rules ALTER COLUMN target_account_type_id DROP NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921102134_RuleTypeExpansionCr02') THEN
    ALTER TABLE rules ADD CONSTRAINT "FK_rules_account_types_target_account_type_id" FOREIGN KEY (target_account_type_id) REFERENCES account_types (id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921102134_RuleTypeExpansionCr02') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921102134_RuleTypeExpansionCr02', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921102638_CashApprovalGateCr04') THEN
    ALTER TABLE rules ADD approved_by character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921102638_CashApprovalGateCr04') THEN
    ALTER TABLE rules ADD created_by character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921102638_CashApprovalGateCr04') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921102638_CashApprovalGateCr04', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921111821_StackingResolutionCr06') THEN
    ALTER TABLE rules ADD exclusivity_group character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921111821_StackingResolutionCr06') THEN
    ALTER TABLE rules ADD stack_mode character varying(20) NOT NULL DEFAULT 'Additive';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921111821_StackingResolutionCr06') THEN
    ALTER TABLE rule_fire_audit ADD resolution_snapshot jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921111821_StackingResolutionCr06') THEN
    UPDATE rules
    SET exclusivity_group = account_types.name
    FROM account_types
    WHERE rules.target_account_type_id = account_types.id
      AND rules.stackable = false
      AND rules.exclusivity_group IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921111821_StackingResolutionCr06') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921111821_StackingResolutionCr06', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921120111_RuleConfigurationCr08') THEN
    ALTER TABLE rules ADD configuration jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921120111_RuleConfigurationCr08') THEN
    CREATE TABLE held_postings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        rule_id uuid NOT NULL,
        customer_account_id uuid NOT NULL,
        contact_key character varying(255) NOT NULL,
        delta numeric(20,4) NOT NULL,
        reason character varying(50) NOT NULL,
        source_event_id character varying(255) NOT NULL,
        idempotency_key character varying(255) NOT NULL,
        metadata jsonb,
        hold_until timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        posted_at timestamp with time zone,
        ledger_entry_id uuid,
        CONSTRAINT "PK_held_postings" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921120111_RuleConfigurationCr08') THEN
    CREATE INDEX idx_held_postings_due ON held_postings (hold_until, posted_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921120111_RuleConfigurationCr08') THEN
    CREATE UNIQUE INDEX ux_held_postings_tenant_idempotency ON held_postings (tenant_id, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921120111_RuleConfigurationCr08') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921120111_RuleConfigurationCr08', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921122237_EngineGuaranteesCr09') THEN
    ALTER TABLE rules ADD current_version integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921122237_EngineGuaranteesCr09') THEN
    ALTER TABLE rule_fire_audit DROP COLUMN rule_version;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921122237_EngineGuaranteesCr09') THEN
    ALTER TABLE rule_fire_audit ADD rule_version integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921122237_EngineGuaranteesCr09') THEN
    CREATE TABLE rule_limit_counters (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        rule_id uuid NOT NULL,
        counter_type character varying(30) NOT NULL,
        period_key character varying(20) NOT NULL,
        value numeric(20,4) NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_rule_limit_counters" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921122237_EngineGuaranteesCr09') THEN
    CREATE TABLE rule_versions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        rule_id uuid NOT NULL,
        version_number integer NOT NULL,
        name character varying(255) NOT NULL,
        type character varying(50) NOT NULL,
        trigger character varying(100) NOT NULL,
        conditions jsonb,
        calculation jsonb NOT NULL,
        target_account_type_id uuid,
        limits jsonb,
        configuration jsonb,
        priority integer NOT NULL,
        stackable boolean NOT NULL,
        exclusivity_group character varying(100),
        stack_mode character varying(20) NOT NULL,
        active_from timestamp with time zone,
        active_to timestamp with time zone,
        effective_from timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_rule_versions" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921122237_EngineGuaranteesCr09') THEN
    CREATE UNIQUE INDEX ux_rule_limit_counters_key ON rule_limit_counters (tenant_id, rule_id, counter_type, period_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921122237_EngineGuaranteesCr09') THEN
    CREATE UNIQUE INDEX ux_rule_versions_tenant_rule_version ON rule_versions (tenant_id, rule_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921122237_EngineGuaranteesCr09') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921122237_EngineGuaranteesCr09', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921161222_CustomerBirthdayCr10') THEN
    CREATE TABLE customer_birthdays (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        contact_key character varying(255) NOT NULL,
        month_day character varying(5) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_customer_birthdays" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921161222_CustomerBirthdayCr10') THEN
    CREATE INDEX idx_customer_birthdays_month_day ON customer_birthdays (tenant_id, month_day);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921161222_CustomerBirthdayCr10') THEN
    CREATE UNIQUE INDEX ux_customer_birthdays_tenant_contact ON customer_birthdays (tenant_id, contact_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260921161222_CustomerBirthdayCr10') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260921161222_CustomerBirthdayCr10', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928185345_AccountTypeConfigCl13') THEN
    ALTER TABLE account_types ADD is_tier_qualifying boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928185345_AccountTypeConfigCl13') THEN
    UPDATE account_types at
    SET is_tier_qualifying = true
    FROM programs p
    WHERE p.qualifying_account_type_id = at.id
      AND p.id = at.program_id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928185345_AccountTypeConfigCl13') THEN
    UPDATE account_types at
    SET config = jsonb_set(at.config, '{warning_days}', to_jsonb(p.warning_days))
    FROM programs p
    WHERE p.id = at.program_id
      AND at.type = 'POINTS'
      AND p.warning_days IS NOT NULL
      AND p.warning_days > 0
      AND jsonb_typeof(at.config->'expiration_days') = 'number'
      AND p.warning_days < (at.config->>'expiration_days')::numeric
      AND NOT (at.config ? 'warning_days');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928185345_AccountTypeConfigCl13') THEN
    UPDATE account_types
    SET config = config - 'expiration_days'
    WHERE type = 'CASH'
      AND config ? 'expiration_days';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928185345_AccountTypeConfigCl13') THEN
    CREATE UNIQUE INDEX ux_account_types_tier_qualifying ON account_types (tenant_id, program_id) WHERE is_tier_qualifying;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928185345_AccountTypeConfigCl13') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260928185345_AccountTypeConfigCl13', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928190845_RuleStackingRetirementCl13') THEN
    INSERT INTO rule_versions (
        id, tenant_id, rule_id, version_number, name, type, trigger, conditions,
        calculation, target_account_type_id, limits, configuration, priority,
        stackable, exclusivity_group, stack_mode, active_from, active_to,
        effective_from, created_at)
    SELECT
        gen_random_uuid(), r.tenant_id, r.id, r.current_version, r.name, r.type, r.trigger, r.conditions,
        r.calculation, r.target_account_type_id, r.limits, r.configuration, r.priority,
        r.stackable, r.exclusivity_group, r.stack_mode, r.active_from, r.active_to,
        now(), now()
    FROM rules r
    WHERE (exclusivity_group IS NOT NULL OR stack_mode <> 'Additive');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928190845_RuleStackingRetirementCl13') THEN
    UPDATE rules
    SET exclusivity_group = NULL,
        stack_mode        = 'Additive',
        current_version   = current_version + 1,
        updated_at        = now()
    WHERE (exclusivity_group IS NOT NULL OR stack_mode <> 'Additive');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928190845_RuleStackingRetirementCl13') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260928190845_RuleStackingRetirementCl13', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928191319_ProgramPublicationCl13') THEN
    ALTER TABLE programs ADD has_unpublished_changes boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928191319_ProgramPublicationCl13') THEN
    ALTER TABLE programs ADD publication_status character varying(20) NOT NULL DEFAULT 'published';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928191319_ProgramPublicationCl13') THEN
    ALTER TABLE programs ADD published_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928191319_ProgramPublicationCl13') THEN
    ALTER TABLE programs ADD published_by character varying(255);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928191319_ProgramPublicationCl13') THEN
    ALTER TABLE programs ADD published_version integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260928191319_ProgramPublicationCl13') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260928191319_ProgramPublicationCl13', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001083819_BurnTriggerRuleTypesCr0930') THEN
    UPDATE programs
    SET has_unpublished_changes = true
    WHERE publication_status = 'published'
      AND id IN (SELECT program_id FROM rules WHERE status IN ('active', 'pending_approval')
    AND ((trigger = 'points.transfer' AND type <> 'TransferRule')
      OR (trigger = 'points.redeem' AND type <> 'RedemptionRule')
      OR trigger = 'reward.purchase'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001083819_BurnTriggerRuleTypesCr0930') THEN
    UPDATE rules
    SET status     = 'disabled',
        updated_at = now()
    WHERE status IN ('active', 'pending_approval')
    AND ((trigger = 'points.transfer' AND type <> 'TransferRule')
      OR (trigger = 'points.redeem' AND type <> 'RedemptionRule')
      OR trigger = 'reward.purchase');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001083819_BurnTriggerRuleTypesCr0930') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001083819_BurnTriggerRuleTypesCr0930', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    ALTER TABLE reward_definitions ADD approved_by character varying(255);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    ALTER TABLE reward_definitions ADD created_by character varying(255);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    ALTER TABLE reward_definitions ADD status character varying(20) NOT NULL DEFAULT 'active';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    ALTER TABLE customer_accounts ADD tier_locked_until date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    ALTER TABLE programs ADD slug character varying(40);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    WITH base AS (
        SELECT id, tenant_id, created_at,
               trim(both '-' from left(trim(both '-' from regexp_replace(lower(name), '[^a-z0-9]+', '-', 'g')), 32)) AS s
        FROM programs
    ),
    fixed AS (
        SELECT id, tenant_id, created_at,
               CASE WHEN length(s) < 2 THEN 'program-' || s ELSE s END AS s
        FROM base
    ),
    numbered AS (
        SELECT id, s, row_number() OVER (PARTITION BY tenant_id, s ORDER BY created_at, id) AS n
        FROM fixed
    )
    UPDATE programs p
    SET slug = CASE WHEN n.n = 1 THEN trim(both '-' from n.s)
                    ELSE trim(both '-' from n.s) || '-' || left(replace(p.id::text, '-', ''), 6) END
    FROM numbered n
    WHERE p.id = n.id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    ALTER TABLE programs ALTER COLUMN slug SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    ALTER TABLE programs ALTER COLUMN slug SET DEFAULT ('p-' || left(replace(gen_random_uuid()::text, '-', ''), 10));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    CREATE UNIQUE INDEX ux_programs_tenant_slug ON programs (tenant_id, slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    UPDATE programs
    SET has_unpublished_changes = true
    WHERE publication_status = 'published'
      AND id IN (SELECT program_id FROM reward_definitions WHERE is_active
    AND (acquisition NOT IN ('points_purchase', 'streak_completion')
      OR reward_type NOT IN ('cashback', 'tier_upgrade')
      OR (reward_type = 'tier_upgrade' AND acquisition <> 'streak_completion')
      OR (reward_type = 'cashback' AND NOT (type_config ? 'cash_account_type_id'))));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    UPDATE reward_definitions
    SET is_active = false
    WHERE is_active
    AND (acquisition NOT IN ('points_purchase', 'streak_completion')
      OR reward_type NOT IN ('cashback', 'tier_upgrade')
      OR (reward_type = 'tier_upgrade' AND acquisition <> 'streak_completion')
      OR (reward_type = 'cashback' AND NOT (type_config ? 'cash_account_type_id')));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    CREATE UNIQUE INDEX ux_reward_definitions_tenant_name_active ON reward_definitions (tenant_id, name) WHERE is_active;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261001090316_RewardCatalogFulfilmentCr0930') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261001090316_RewardCatalogFulfilmentCr0930', '8.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002162724_CustomerViewCr1002') THEN
    ALTER TABLE event_inbox ADD contact_key character varying(255);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002162724_CustomerViewCr1002') THEN
    CREATE INDEX idx_streak_progress_tenant_contact ON streak_progress (tenant_id, contact_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002162724_CustomerViewCr1002') THEN
    CREATE INDEX idx_streak_log_tenant_contact ON streak_log (tenant_id, contact_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002162724_CustomerViewCr1002') THEN
    CREATE INDEX idx_rule_fire_audit_tenant_contact_date ON rule_fire_audit (tenant_id, contact_key, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002162724_CustomerViewCr1002') THEN
    CREATE INDEX idx_outbox_tenant_contact_date ON outbox_events (tenant_id, contact_key, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002162724_CustomerViewCr1002') THEN
    CREATE INDEX idx_held_postings_tenant_contact_date ON held_postings (tenant_id, contact_key, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002162724_CustomerViewCr1002') THEN
    CREATE INDEX idx_event_inbox_tenant_contact_received ON event_inbox (tenant_id, contact_key, received_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261002162724_CustomerViewCr1002') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261002162724_CustomerViewCr1002', '8.0.0');
    END IF;
END $EF$;
COMMIT;

