using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RulesSoftDeleteTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defense against physical DELETE: if a DELETE FROM rules is run directly on the DB,
            // the trigger converts it into a status='deleted' + updated_at=NOW() UPDATE.
            // Result: deleting a rule always produces an UpdatedAt → delta sync catches it (max 30s SLA).
            //
            // To keep the trigger from firing over and over: if the row is ALREADY status='deleted',
            // we allow the physical DELETE — so manual cleanup / GDPR deletion remains possible.
            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_rules_soft_delete ON rules;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS rules_soft_delete_trg();");
        }
    }
}
