using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class PerfisColaboradores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_perfis_colaboradores",
                columns: table => new
                {
                    id_pk_perfil_colaborador = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_nome_exibicao = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ds_cargo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ds_ramal = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ds_sobre = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    id_fk_setor = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    gestor_usuario_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    dt_updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_perfis_colaboradores", x => x.id_pk_perfil_colaborador);
                    table.ForeignKey(
                        name: "fk_perfis_colaboradores_setor",
                        column: x => x.id_fk_setor,
                        principalTable: "tb_setores",
                        principalColumn: "id_pk_setor",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_perfis_colaboradores_gestor_usuario_id",
                table: "tb_perfis_colaboradores",
                column: "gestor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "idx_perfis_colaboradores_id_fk_setor",
                table: "tb_perfis_colaboradores",
                column: "id_fk_setor");

            migrationBuilder.CreateIndex(
                name: "uk_perfis_colaboradores_usuario_id",
                table: "tb_perfis_colaboradores",
                column: "usuario_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_perfis_colaboradores");
        }
    }
}
