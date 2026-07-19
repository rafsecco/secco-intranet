using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_setores",
                columns: table => new
                {
                    id_pk_setor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_nome = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ds_slug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    fl_fixo = table.Column<bool>(type: "bit", nullable: false),
                    fl_ativo = table.Column<bool>(type: "bit", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_setores", x => x.id_pk_setor);
                });

            migrationBuilder.CreateIndex(
                name: "idx_setores_ds_nome",
                table: "tb_setores",
                column: "ds_nome");

            migrationBuilder.CreateIndex(
                name: "idx_setores_fl_ativo",
                table: "tb_setores",
                column: "fl_ativo");

            migrationBuilder.CreateIndex(
                name: "uk_setores_ds_slug",
                table: "tb_setores",
                column: "ds_slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_setores");
        }
    }
}
