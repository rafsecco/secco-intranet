using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class PublicacaoImagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ds_imagem_caminho_relativo",
                table: "tb_publicacoes",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ds_imagem_chave_embrulhada",
                table: "tb_publicacoes",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ds_imagem_descricao",
                table: "tb_publicacoes",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ds_imagem_caminho_relativo",
                table: "tb_publicacoes");

            migrationBuilder.DropColumn(
                name: "ds_imagem_chave_embrulhada",
                table: "tb_publicacoes");

            migrationBuilder.DropColumn(
                name: "ds_imagem_descricao",
                table: "tb_publicacoes");
        }
    }
}
