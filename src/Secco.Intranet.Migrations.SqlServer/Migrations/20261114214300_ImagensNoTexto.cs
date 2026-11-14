using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ImagensNoTexto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_imagens_no_texto",
                columns: table => new
                {
                    id_pk_imagem_no_texto = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_publicacao = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_caminho_relativo = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ds_chave_embrulhada = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imagens_no_texto", x => x.id_pk_imagem_no_texto);
                    table.ForeignKey(
                        name: "fk_imagens_no_texto_publicacao",
                        column: x => x.id_fk_publicacao,
                        principalTable: "tb_publicacoes",
                        principalColumn: "id_pk_publicacao",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_imagens_no_texto_id_fk_publicacao",
                table: "tb_imagens_no_texto",
                column: "id_fk_publicacao");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_imagens_no_texto");
        }
    }
}
