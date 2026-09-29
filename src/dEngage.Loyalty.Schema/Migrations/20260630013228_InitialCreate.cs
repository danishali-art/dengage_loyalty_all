using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Non-partitioned tables (EF-managed) ---

            migrationBuilder.CreateTable(
                name: "programs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_programs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    config = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_types", x => x.id);
                    table.ForeignKey(
                        name: "FK_account_types_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contact_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    account_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(20,4)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_accounts_account_types_account_type_id",
                        column: x => x.account_type_id,
                        principalTable: "account_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trigger = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: true),
                    calculation = table.Column<string>(type: "jsonb", nullable: false),
                    target_account_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    limits = table.Column<string>(type: "jsonb", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    stackable = table.Column<bool>(type: "boolean", nullable: false),
                    active_from = table.Column<DateOnly>(type: "date", nullable: true),
                    active_to = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_rules_account_types_target_account_type_id",
                        column: x => x.target_account_type_id,
                        principalTable: "account_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rules_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reward_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contact_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    account_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reward_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ledger_reset_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completion_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_reward_log_account_types_account_type_id",
                        column: x => x.account_type_id,
                        principalTable: "account_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    // No FK to ledger_entries: PostgreSQL doesn't allow FKs referencing partitioned tables.
                    // Integrity enforced at application level via LedgerResetEntryId.
                });

            // --- Partitioned tables (raw SQL) ---
            // ledger_entries: PARTITION BY LIST (tenant_id)
            // Unique index must include partition key per PostgreSQL requirement.
            // No FK from reward_log: PostgreSQL limitation on partitioned tables as FK targets.
            migrationBuilder.Sql(@"
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
CREATE INDEX ""IX_ledger_entries_customer_account_id""
    ON ledger_entries (customer_account_id);
CREATE INDEX ""IX_ledger_entries_rule_id""
    ON ledger_entries (rule_id);
");

            // event_inbox: PARTITION BY LIST (tenant_id)
            migrationBuilder.Sql(@"
CREATE TABLE event_inbox (
    event_id        character varying(255)      NOT NULL,
    tenant_id       character varying(100)      NOT NULL,
    event_type      character varying(50)       NOT NULL,
    payload         jsonb                       NOT NULL,
    received_at     timestamp with time zone    NOT NULL,
    processed_at    timestamp with time zone,
    status          character varying(20)       NOT NULL,
    error           text,
    CONSTRAINT ""PK_event_inbox"" PRIMARY KEY (tenant_id, event_id)
) PARTITION BY LIST (tenant_id);
");

            // --- Indexes for non-partitioned tables ---

            migrationBuilder.CreateIndex(
                name: "idx_account_types_tenant_program",
                table: "account_types",
                columns: new[] { "tenant_id", "program_id" });

            migrationBuilder.CreateIndex(
                name: "IX_account_types_program_id",
                table: "account_types",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "idx_customer_accounts_tenant_contact",
                table: "customer_accounts",
                columns: new[] { "tenant_id", "contact_key" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_accounts_account_type_id",
                table: "customer_accounts",
                column: "account_type_id");

            migrationBuilder.CreateIndex(
                name: "uq_customer_accounts_tenant_contact_accounttype",
                table: "customer_accounts",
                columns: new[] { "tenant_id", "contact_key", "account_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_programs_tenant_id",
                table: "programs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "idx_reward_log_tenant_contact_status",
                table: "reward_log",
                columns: new[] { "tenant_id", "contact_key", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_reward_log_account_type_id",
                table: "reward_log",
                column: "account_type_id");

            migrationBuilder.CreateIndex(
                name: "idx_rules_tenant_program_status",
                table: "rules",
                columns: new[] { "tenant_id", "program_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_rules_program_id",
                table: "rules",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "IX_rules_target_account_type_id",
                table: "rules",
                column: "target_account_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "event_inbox");
            migrationBuilder.DropTable(name: "ledger_entries");
            migrationBuilder.DropTable(name: "reward_log");
            migrationBuilder.DropTable(name: "customer_accounts");
            migrationBuilder.DropTable(name: "rules");
            migrationBuilder.DropTable(name: "account_types");
            migrationBuilder.DropTable(name: "programs");
        }
    }
}
