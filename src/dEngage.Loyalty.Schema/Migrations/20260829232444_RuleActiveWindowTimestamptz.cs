using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RuleActiveWindowTimestamptz : Migration
    {
        // Explicit USING keeps the conversion session-TZ-independent (an implicit
        // date→timestamptz cast would use the session timezone). active_to +1 day:
        // the old inclusive-day upper bound becomes an exclusive timestamp.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE rules
                    ALTER COLUMN active_from TYPE timestamp with time zone
                        USING (active_from::timestamp AT TIME ZONE 'UTC'),
                    ALTER COLUMN active_to TYPE timestamp with time zone
                        USING ((active_to + 1)::timestamp AT TIME ZONE 'UTC');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("RuleActiveWindowTimestamptz is a one-way migration (hour-precision windows would be lost).");
        }
    }
}
