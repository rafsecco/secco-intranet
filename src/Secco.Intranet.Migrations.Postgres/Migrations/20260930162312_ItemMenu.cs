using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ItemMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_itens_menu",
                columns: table => new
                {
                    id_pk_item_menu = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_setor = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_parent = table.Column<Guid>(type: "uuid", nullable: true),
                    ds_nome = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ds_slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ie_tipo = table.Column<int>(type: "integer", nullable: false),
                    ds_rota = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ds_icone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    nr_ordem = table.Column<int>(type: "integer", nullable: false),
                    fl_ativo = table.Column<bool>(type: "boolean", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_menu", x => x.id_pk_item_menu);
                    table.ForeignKey(
                        name: "fk_itens_menu_item_menu",
                        column: x => x.id_fk_parent,
                        principalTable: "tb_itens_menu",
                        principalColumn: "id_pk_item_menu",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_itens_menu_setor",
                        column: x => x.id_fk_setor,
                        principalTable: "tb_setores",
                        principalColumn: "id_pk_setor",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_itens_menu_id_fk_parent_ds_slug",
                table: "tb_itens_menu",
                columns: new[] { "id_fk_parent", "ds_slug" });

            migrationBuilder.CreateIndex(
                name: "idx_itens_menu_id_fk_setor",
                table: "tb_itens_menu",
                column: "id_fk_setor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_itens_menu");
        }
    }
}
