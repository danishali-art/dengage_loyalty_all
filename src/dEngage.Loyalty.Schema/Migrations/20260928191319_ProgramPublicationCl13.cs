using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class ProgramPublicationCl13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_unpublished_changes",
                table: "programs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 1.3.CL item 8: every program that exists today is already live config, so it is
            // backfilled as 'published' (has_unpublished_changes = false, no snapshot yet — its
            // first ProgramPublication version is written on the next Publish). The DB default
            // stays 'published' so raw-SQL inserts (scripts/*.sql tenant seeds, TestCli) keep
            // producing runnable programs; the API always sets the column explicitly and creates
            // new programs as 'draft'.
            migrationBuilder.AddColumn<string>(
                name: "publication_status",
                table: "programs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "published");

            migrationBuilder.AddColumn<DateTime>(
                name: "published_at",
                table: "programs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "published_by",
                table: "programs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "published_version",
                table: "programs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_unpublished_changes",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "publication_status",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "published_by",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "published_version",
                table: "programs");
        }
    }
}
