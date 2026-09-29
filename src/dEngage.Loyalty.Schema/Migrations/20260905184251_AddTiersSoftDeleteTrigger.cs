using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class AddTiersSoftDeleteTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same defense-in-depth pattern as rules_soft_delete_trg (see
            // 20260702024651_RulesSoftDeleteTrigger.cs): a raw DELETE FROM tier_definitions
            // run outside the app becomes a status='deleted' UPDATE instead.
            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_tier_definitions_soft_delete ON tier_definitions;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tier_definitions_soft_delete_trg();");
        }
    }
}
