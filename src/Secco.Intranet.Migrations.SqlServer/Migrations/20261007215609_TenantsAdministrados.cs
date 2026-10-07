using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class TenantsAdministrados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_tenants_administrados",
                columns: table => new
                {
                    id_pk_tenant_administrado = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_sistema = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ds_responsavel = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ie_origem = table.Column<int>(type: "int", nullable: false),
                    fl_secure_gate_habilitado = table.Column<bool>(type: "bit", nullable: false),
                    ds_registrado_por = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dt_updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants_administrados", x => x.id_pk_tenant_administrado);
                });

            migrationBuilder.CreateIndex(
                name: "uk_tenants_administrados_tenant_id",
                table: "tb_tenants_administrados",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_tenants_administrados");
        }
    }
}
