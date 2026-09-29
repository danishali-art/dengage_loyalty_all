CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

CREATE TABLE programs (
    id uuid NOT NULL,
    tenant_id character varying(100) NOT NULL,
    name character varying(255) NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_programs" PRIMARY KEY (id)
);

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


CREATE INDEX idx_account_types_tenant_program ON account_types (tenant_id, program_id);

CREATE INDEX "IX_account_types_program_id" ON account_types (program_id);

CREATE INDEX idx_customer_accounts_tenant_contact ON customer_accounts (tenant_id, contact_key);

CREATE INDEX "IX_customer_accounts_account_type_id" ON customer_accounts (account_type_id);

CREATE UNIQUE INDEX uq_customer_accounts_tenant_contact_accounttype ON customer_accounts (tenant_id, contact_key, account_type_id);

CREATE INDEX idx_programs_tenant_id ON programs (tenant_id);

CREATE INDEX idx_reward_log_tenant_contact_status ON reward_log (tenant_id, contact_key, status);

CREATE INDEX "IX_reward_log_account_type_id" ON reward_log (account_type_id);

CREATE INDEX idx_rules_tenant_program_status ON rules (tenant_id, program_id, status);

CREATE INDEX "IX_rules_program_id" ON rules (program_id);

CREATE INDEX "IX_rules_target_account_type_id" ON rules (target_account_type_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260630013228_InitialCreate', '8.0.0');

COMMIT;

START TRANSACTION;

ALTER TABLE programs ADD qualifying_account_type_id uuid;

ALTER TABLE customer_accounts ADD tier_expires_at date;

ALTER TABLE customer_accounts ADD tier_id uuid;

ALTER TABLE customer_accounts ADD tier_period_start date;

ALTER TABLE customer_accounts ADD tier_qualifying_pts numeric(20,4) NOT NULL DEFAULT 0.0;

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

CREATE INDEX "IX_programs_qualifying_account_type_id" ON programs (qualifying_account_type_id);

CREATE INDEX "IX_customer_accounts_tier_id" ON customer_accounts (tier_id);

CREATE INDEX idx_tier_definitions_tenant_program ON tier_definitions (tenant_id, program_id);

CREATE UNIQUE INDEX uq_tier_definitions_program_name ON tier_definitions (program_id, name);

CREATE INDEX idx_tier_upgrade_log_tenant_contact ON tier_upgrade_log (tenant_id, contact_key);

CREATE INDEX "IX_tier_upgrade_log_from_tier_id" ON tier_upgrade_log (from_tier_id);

CREATE INDEX "IX_tier_upgrade_log_to_tier_id" ON tier_upgrade_log (to_tier_id);

ALTER TABLE customer_accounts ADD CONSTRAINT "FK_customer_accounts_tier_definitions_tier_id" FOREIGN KEY (tier_id) REFERENCES tier_definitions (id);

ALTER TABLE programs ADD CONSTRAINT "FK_programs_account_types_qualifying_account_type_id" FOREIGN KEY (qualifying_account_type_id) REFERENCES account_types (id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260701184514_TierSystem', '8.0.0');

COMMIT;

START TRANSACTION;

CREATE UNIQUE INDEX ux_tier_upgrade_log_source_event ON tier_upgrade_log (tenant_id, contact_key, source_event_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260701210128_TierUpgradeLogSourceEventUnique', '8.0.0');

COMMIT;

START TRANSACTION;

CREATE OR REPLACE FUNCTION rules_soft_delete_trg()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.status = 'deleted' THEN
        RETURN OLD;  -- purge: gerçekten sil
    END IF;

    UPDATE rules
       SET status     = 'deleted',
           updated_at = NOW()
     WHERE id = OLD.id;

    RETURN NULL;  -- fiziksel DELETE'i iptal et
END;
$$;

DROP TRIGGER IF EXISTS trg_rules_soft_delete ON rules;
CREATE TRIGGER trg_rules_soft_delete
    BEFORE DELETE ON rules
    FOR EACH ROW
    EXECUTE FUNCTION rules_soft_delete_trg();

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260702024651_RulesSoftDeleteTrigger', '8.0.0');

COMMIT;

START TRANSACTION;

CREATE UNIQUE INDEX ux_reward_log_source_event ON reward_log (tenant_id, account_type_id, source_event_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260705202945_RewardLogSourceEventUnique', '8.0.0');

COMMIT;

START TRANSACTION;

ALTER TABLE reward_log ADD reward_definition_id uuid;

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

CREATE INDEX "IX_reward_log_reward_definition_id" ON reward_log (reward_definition_id);

CREATE INDEX idx_reward_definitions_tenant_program ON reward_definitions (tenant_id, program_id);

CREATE INDEX "IX_reward_definitions_points_account_type_id" ON reward_definitions (points_account_type_id);

CREATE INDEX "IX_reward_definitions_program_id" ON reward_definitions (program_id);

CREATE INDEX "IX_reward_definitions_stamp_account_type_id" ON reward_definitions (stamp_account_type_id);

CREATE UNIQUE INDEX uq_reward_definitions_tenant_program_name ON reward_definitions (tenant_id, program_id, name);

CREATE UNIQUE INDEX ux_reward_definitions_active_stamp ON reward_definitions (tenant_id, stamp_account_type_id) WHERE acquisition = 'stamp_completion' AND is_active;

ALTER TABLE reward_log ADD CONSTRAINT "FK_reward_log_reward_definitions_reward_definition_id" FOREIGN KEY (reward_definition_id) REFERENCES reward_definitions (id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260705235304_RewardDefinitions', '8.0.0');

COMMIT;

START TRANSACTION;

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

CREATE INDEX idx_outbox_pending_next_attempt ON outbox_events (next_attempt_at) WHERE status = 'pending';

CREATE UNIQUE INDEX ux_outbox_dedup ON outbox_events (tenant_id, dedup_key) WHERE dedup_key IS NOT NULL;

CREATE UNIQUE INDEX ux_outbox_event_id ON outbox_events (event_id);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260706003307_OutboxEvents', '8.0.0');

COMMIT;

START TRANSACTION;

ALTER TABLE programs ADD warning_days integer;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260706070750_ProgramWarningDays', '8.0.0');

COMMIT;

