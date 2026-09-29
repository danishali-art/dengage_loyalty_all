using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dEngage.Loyalty.Schema.Migrations
{
    /// <inheritdoc />
    public partial class TenantGuidSurrogateKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // tenants.id (varchar slug, PK) -> tenants.slug (varchar, unique) + new tenants.id
            // (uuid, PK). The slug stays the external identity (URLs/JWT/RabbitMQ/API keys/
            // partition names for ledger_entries/event_inbox/event_log); id becomes the internal
            // join key every other tenant-scoped table's FK now targets.
            migrationBuilder.DropForeignKey(name: "FK_admin_users_tenants_tenant_id", table: "admin_users");
            migrationBuilder.DropForeignKey(name: "FK_tenant_api_keys_tenants_tenant_id", table: "tenant_api_keys");
            migrationBuilder.DropIndex(name: "idx_admin_users_tenant_id", table: "admin_users");
            migrationBuilder.DropIndex(name: "idx_tenant_api_keys_tenant_id", table: "tenant_api_keys");

            migrationBuilder.Sql("ALTER TABLE tenants DROP CONSTRAINT \"PK_tenants\";");
            migrationBuilder.RenameColumn(name: "id", table: "tenants", newName: "slug");
            migrationBuilder.AddColumn<Guid>(name: "id", table: "tenants", type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()");
            migrationBuilder.Sql("ALTER TABLE tenants ALTER COLUMN id DROP DEFAULT;");
            migrationBuilder.AddPrimaryKey(name: "PK_tenants", table: "tenants", column: "id");
            migrationBuilder.CreateIndex(name: "ix_tenants_slug", table: "tenants", column: "slug", unique: true);

            // admin_users.tenant_id: varchar (slug, nullable — null for platform admins) -> uuid
            migrationBuilder.AddColumn<Guid>(name: "tenant_id_new", table: "admin_users", type: "uuid", nullable: true);
            migrationBuilder.Sql("""
                UPDATE admin_users a SET tenant_id_new = t.id
                FROM tenants t WHERE t.slug = a.tenant_id;
                """);
            migrationBuilder.DropColumn(name: "tenant_id", table: "admin_users");
            migrationBuilder.RenameColumn(name: "tenant_id_new", table: "admin_users", newName: "tenant_id");
            migrationBuilder.CreateIndex(name: "idx_admin_users_tenant_id", table: "admin_users", column: "tenant_id");
            migrationBuilder.AddForeignKey(name: "FK_admin_users_tenants_tenant_id", table: "admin_users", column: "tenant_id", principalTable: "tenants", principalColumn: "id");

            // tenant_api_keys.tenant_id: varchar (slug, required) -> uuid
            migrationBuilder.AddColumn<Guid>(name: "tenant_id_new", table: "tenant_api_keys", type: "uuid", nullable: true);
            migrationBuilder.Sql("""
                UPDATE tenant_api_keys k SET tenant_id_new = t.id
                FROM tenants t WHERE t.slug = k.tenant_id;
                """);
            migrationBuilder.Sql("ALTER TABLE tenant_api_keys ALTER COLUMN tenant_id_new SET NOT NULL;");
            migrationBuilder.DropColumn(name: "tenant_id", table: "tenant_api_keys");
            migrationBuilder.RenameColumn(name: "tenant_id_new", table: "tenant_api_keys", newName: "tenant_id");
            migrationBuilder.CreateIndex(name: "idx_tenant_api_keys_tenant_id", table: "tenant_api_keys", column: "tenant_id");
            migrationBuilder.AddForeignKey(name: "FK_tenant_api_keys_tenants_tenant_id", table: "tenant_api_keys", column: "tenant_id", principalTable: "tenants", principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_admin_users_tenants_tenant_id", table: "admin_users");
            migrationBuilder.DropForeignKey(name: "FK_tenant_api_keys_tenants_tenant_id", table: "tenant_api_keys");
            migrationBuilder.DropIndex(name: "idx_admin_users_tenant_id", table: "admin_users");
            migrationBuilder.DropIndex(name: "idx_tenant_api_keys_tenant_id", table: "tenant_api_keys");

            migrationBuilder.AddColumn<string>(name: "tenant_id_old", table: "admin_users", type: "character varying(50)", maxLength: 50, nullable: true);
            migrationBuilder.Sql("UPDATE admin_users a SET tenant_id_old = t.slug FROM tenants t WHERE t.id = a.tenant_id;");
            migrationBuilder.DropColumn(name: "tenant_id", table: "admin_users");
            migrationBuilder.RenameColumn(name: "tenant_id_old", table: "admin_users", newName: "tenant_id");
            migrationBuilder.CreateIndex(name: "idx_admin_users_tenant_id", table: "admin_users", column: "tenant_id");

            migrationBuilder.AddColumn<string>(name: "tenant_id_old", table: "tenant_api_keys", type: "character varying(50)", maxLength: 50, nullable: true);
            migrationBuilder.Sql("UPDATE tenant_api_keys k SET tenant_id_old = t.slug FROM tenants t WHERE t.id = k.tenant_id;");
            migrationBuilder.DropColumn(name: "tenant_id", table: "tenant_api_keys");
            migrationBuilder.Sql("ALTER TABLE tenant_api_keys ALTER COLUMN tenant_id_old SET NOT NULL;");
            migrationBuilder.RenameColumn(name: "tenant_id_old", table: "tenant_api_keys", newName: "tenant_id");
            migrationBuilder.CreateIndex(name: "idx_tenant_api_keys_tenant_id", table: "tenant_api_keys", column: "tenant_id");

            migrationBuilder.DropIndex(name: "ix_tenants_slug", table: "tenants");
            migrationBuilder.Sql("ALTER TABLE tenants DROP CONSTRAINT \"PK_tenants\";");
            migrationBuilder.DropColumn(name: "id", table: "tenants");
            migrationBuilder.RenameColumn(name: "slug", table: "tenants", newName: "id");
            migrationBuilder.AddPrimaryKey(name: "PK_tenants", table: "tenants", column: "id");

            migrationBuilder.AddForeignKey(name: "FK_admin_users_tenants_tenant_id", table: "admin_users", column: "tenant_id", principalTable: "tenants", principalColumn: "id");
            migrationBuilder.AddForeignKey(name: "FK_tenant_api_keys_tenants_tenant_id", table: "tenant_api_keys", column: "tenant_id", principalTable: "tenants", principalColumn: "id");
        }
    }
}
