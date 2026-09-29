using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RuleTypeExpansionCr02 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_rules_account_types_target_account_type_id",
                table: "rules");

            migrationBuilder.AlterColumn<Guid>(
                name: "target_account_type_id",
                table: "rules",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_rules_account_types_target_account_type_id",
                table: "rules",
                column: "target_account_type_id",
                principalTable: "account_types",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_rules_account_types_target_account_type_id",
                table: "rules");

            migrationBuilder.AlterColumn<Guid>(
                name: "target_account_type_id",
                table: "rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_rules_account_types_target_account_type_id",
                table: "rules",
                column: "target_account_type_id",
                principalTable: "account_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
