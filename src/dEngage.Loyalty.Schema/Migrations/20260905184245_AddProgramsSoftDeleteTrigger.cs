using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramsSoftDeleteTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same defense-in-depth pattern as rules_soft_delete_trg (see
            // 20260702024651_RulesSoftDeleteTrigger.cs): a raw DELETE FROM programs run
            // outside the app becomes a status='deleted' UPDATE instead.
            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_programs_soft_delete ON programs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS programs_soft_delete_trg();");
        }
    }
}
