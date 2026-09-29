using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class RewardDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reward_definition_id",
                table: "reward_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reward_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    acquisition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    stamp_account_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    points_price = table.Column<decimal>(type: "numeric(20,4)", nullable: true),
                    points_account_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_coupon_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_reward_definitions_account_types_points_account_type_id",
                        column: x => x.points_account_type_id,
                        principalTable: "account_types",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_reward_definitions_account_types_stamp_account_type_id",
                        column: x => x.stamp_account_type_id,
                        principalTable: "account_types",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_reward_definitions_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reward_log_reward_definition_id",
                table: "reward_log",
                column: "reward_definition_id");

            migrationBuilder.CreateIndex(
                name: "idx_reward_definitions_tenant_program",
                table: "reward_definitions",
                columns: new[] { "tenant_id", "program_id" });

            migrationBuilder.CreateIndex(
                name: "IX_reward_definitions_points_account_type_id",
                table: "reward_definitions",
                column: "points_account_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_reward_definitions_program_id",
                table: "reward_definitions",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "IX_reward_definitions_stamp_account_type_id",
                table: "reward_definitions",
                column: "stamp_account_type_id");

            migrationBuilder.CreateIndex(
                name: "uq_reward_definitions_tenant_program_name",
                table: "reward_definitions",
                columns: new[] { "tenant_id", "program_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_reward_definitions_active_stamp",
                table: "reward_definitions",
                columns: new[] { "tenant_id", "stamp_account_type_id" },
                unique: true,
                filter: "acquisition = 'stamp_completion' AND is_active");

            migrationBuilder.AddForeignKey(
                name: "FK_reward_log_reward_definitions_reward_definition_id",
                table: "reward_log",
                column: "reward_definition_id",
                principalTable: "reward_definitions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reward_log_reward_definitions_reward_definition_id",
                table: "reward_log");

            migrationBuilder.DropTable(
                name: "reward_definitions");

            migrationBuilder.DropIndex(
                name: "IX_reward_log_reward_definition_id",
                table: "reward_log");

            migrationBuilder.DropColumn(
                name: "reward_definition_id",
                table: "reward_log");
        }
    }
}
