using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class FotoDoColaborador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ds_foto_caminho_relativo",
                table: "tb_perfis_colaboradores",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ds_foto_chave_embrulhada",
                table: "tb_perfis_colaboradores",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ds_foto_caminho_relativo",
                table: "tb_perfis_colaboradores");

            migrationBuilder.DropColumn(
                name: "ds_foto_chave_embrulhada",
                table: "tb_perfis_colaboradores");
        }
    }
}
