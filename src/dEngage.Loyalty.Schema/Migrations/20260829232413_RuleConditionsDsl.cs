using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RuleConditionsDsl : Migration
    {
        // Old object format → DSL v1 clause array. jsonb_typeof guard makes this
        // idempotent: already-converted rows (arrays) are skipped.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("RuleConditionsDsl is a one-way data migration.");
        }
    }
}
