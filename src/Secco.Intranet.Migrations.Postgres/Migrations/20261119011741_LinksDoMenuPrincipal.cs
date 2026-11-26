using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class LinksDoMenuPrincipal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_links_do_menu_principal",
                columns: table => new
                {
                    id_pk_link_do_menu_principal = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_rotulo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ds_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ds_icone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    fl_nova_aba = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_links_do_menu_principal", x => x.id_pk_link_do_menu_principal);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_links_do_menu_principal");
        }
    }
}
