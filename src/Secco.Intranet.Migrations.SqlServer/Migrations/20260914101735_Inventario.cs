using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Inventario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_itens_inventario",
                columns: table => new
                {
                    id_pk_item_inventario = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_nome = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ds_descricao = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    ds_categoria = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ds_codigo_patrimonio = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    id_fk_setor = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ie_status = table.Column<int>(type: "int", nullable: false),
                    atribuido_a_usuario_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ds_atribuido_a_nome = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_inventario", x => x.id_pk_item_inventario);
                    table.ForeignKey(
                        name: "fk_itens_inventario_setor",
                        column: x => x.id_fk_setor,
                        principalTable: "tb_setores",
                        principalColumn: "id_pk_setor",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_itens_inventario_ds_nome",
                table: "tb_itens_inventario",
                column: "ds_nome");

            migrationBuilder.CreateIndex(
                name: "idx_itens_inventario_id_fk_setor",
                table: "tb_itens_inventario",
                column: "id_fk_setor");

            migrationBuilder.CreateIndex(
                name: "idx_itens_inventario_ie_status",
                table: "tb_itens_inventario",
                column: "ie_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_itens_inventario");
        }
    }
}
