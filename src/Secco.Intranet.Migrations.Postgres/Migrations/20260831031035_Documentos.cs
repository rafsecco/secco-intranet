using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Documentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_documentos",
                columns: table => new
                {
                    id_pk_documento = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_setor = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_titulo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ds_descricao = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    ds_nome_arquivo = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ds_content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    nr_tamanho = table.Column<long>(type: "bigint", nullable: false),
                    ie_visibilidade = table.Column<int>(type: "integer", nullable: false),
                    ds_caminho_relativo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ds_chave_embrulhada = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ds_criado_por = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    fl_ativo = table.Column<bool>(type: "boolean", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos", x => x.id_pk_documento);
                    table.ForeignKey(
                        name: "fk_documentos_setor",
                        column: x => x.id_fk_setor,
                        principalTable: "tb_setores",
                        principalColumn: "id_pk_setor",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_documentos_dt_created_at",
                table: "tb_documentos",
                column: "dt_created_at");

            migrationBuilder.CreateIndex(
                name: "idx_documentos_fl_ativo",
                table: "tb_documentos",
                column: "fl_ativo");

            migrationBuilder.CreateIndex(
                name: "idx_documentos_id_fk_setor",
                table: "tb_documentos",
                column: "id_fk_setor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_documentos");
        }
    }
}
