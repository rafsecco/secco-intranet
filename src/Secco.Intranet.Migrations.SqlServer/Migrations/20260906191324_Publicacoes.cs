using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Publicacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_publicacoes",
                columns: table => new
                {
                    id_pk_publicacao = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_setor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_titulo = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ds_corpo = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ie_tipo = table.Column<int>(type: "int", nullable: false),
                    ie_visibilidade = table.Column<int>(type: "int", nullable: false),
                    ie_prioridade = table.Column<int>(type: "int", nullable: false),
                    dt_publicado_em = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dt_expira_em = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    fl_ativo = table.Column<bool>(type: "bit", nullable: false),
                    ds_criado_por = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dt_atualizado_em = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publicacoes", x => x.id_pk_publicacao);
                    table.ForeignKey(
                        name: "fk_publicacoes_setor",
                        column: x => x.id_fk_setor,
                        principalTable: "tb_setores",
                        principalColumn: "id_pk_setor",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_publicacoes_fl_ativo_dt_publicado_em",
                table: "tb_publicacoes",
                columns: new[] { "fl_ativo", "dt_publicado_em" });

            migrationBuilder.CreateIndex(
                name: "idx_publicacoes_id_fk_setor",
                table: "tb_publicacoes",
                column: "id_fk_setor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_publicacoes");
        }
    }
}
