using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class IconeDoSetor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ds_icone",
                table: "tb_setores",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "bi-diagram-3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ds_icone",
                table: "tb_setores");
        }
    }
}
