using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PosicoesMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_posicoes_menu",
                columns: table => new
                {
                    id_pk_posicao_menu = table.Column<Guid>(type: "uuid", nullable: false),
                    ie_grupo = table.Column<int>(type: "integer", nullable: false),
                    ds_chave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nr_ordem = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posicoes_menu", x => x.id_pk_posicao_menu);
                });

            migrationBuilder.CreateIndex(
                name: "uk_posicoes_menu_ie_grupo_ds_chave",
                table: "tb_posicoes_menu",
                columns: new[] { "ie_grupo", "ds_chave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_posicoes_menu");
        }
    }
}
