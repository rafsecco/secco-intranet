using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class RemoverNomeExibicaoDoPerfil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ds_nome_exibicao",
                table: "tb_perfis_colaboradores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ds_nome_exibicao",
                table: "tb_perfis_colaboradores",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);
        }
    }
}
