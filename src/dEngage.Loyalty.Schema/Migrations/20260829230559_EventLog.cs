using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class EventLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // event_log: PARTITION BY LIST (tenant_id) — raw SQL, same pattern as event_inbox.
            // Payload is deliberately NOT stored (12-month retention, occurred_within lookups only).
            migrationBuilder.Sql(@"
CREATE TABLE event_log (
    tenant_id       character varying(100)      NOT NULL,
    event_id        character varying(255)      NOT NULL,
    contact_key     character varying(255)      NOT NULL,
    event_type      character varying(50)       NOT NULL,
    occurred_at     timestamp with time zone    NOT NULL,
    CONSTRAINT ""PK_event_log"" PRIMARY KEY (tenant_id, event_id)
) PARTITION BY LIST (tenant_id);

CREATE INDEX idx_event_log_contact_type_time
    ON event_log (tenant_id, contact_key, event_type, occurred_at DESC);
CREATE INDEX idx_event_log_occurred
    ON event_log (occurred_at);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_log");
        }
    }
}
