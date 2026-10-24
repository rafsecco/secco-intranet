using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Vencimentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_itens_vencimento",
                columns: table => new
                {
                    id_pk_item_vencimento = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_setor = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_titulo = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ds_descricao = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    dt_data_vencimento = table.Column<DateOnly>(type: "date", nullable: false),
                    responsavel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ds_antecedencias = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    nr_ciclo = table.Column<int>(type: "int", nullable: false),
                    dt_encerrado_em = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ds_encerrado_por = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_vencimento", x => x.id_pk_item_vencimento);
                    table.ForeignKey(
                        name: "fk_itens_vencimento_setor",
                        column: x => x.id_fk_setor,
                        principalTable: "tb_setores",
                        principalColumn: "id_pk_setor",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tb_avisos_de_vencimento_enviados",
                columns: table => new
                {
                    id_pk_aviso_de_vencimento_enviado = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_item_vencimento = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nr_ciclo = table.Column<int>(type: "int", nullable: false),
                    nr_marco = table.Column<int>(type: "int", nullable: false),
                    ie_resultado = table.Column<int>(type: "int", nullable: false),
                    nr_quantidade_de_destinos = table.Column<int>(type: "int", nullable: false),
                    dt_tratado_em = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos_de_vencimento_enviados", x => x.id_pk_aviso_de_vencimento_enviado);
                    table.ForeignKey(
                        name: "fk_avisos_de_vencimento_enviados_item_vencimento",
                        column: x => x.id_fk_item_vencimento,
                        principalTable: "tb_itens_vencimento",
                        principalColumn: "id_pk_item_vencimento",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tb_renovacoes_de_vencimento",
                columns: table => new
                {
                    id_pk_renovacao_de_vencimento = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id_fk_item_vencimento = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nr_ciclo_anterior = table.Column<int>(type: "int", nullable: false),
                    dt_data_anterior = table.Column<DateOnly>(type: "date", nullable: false),
                    dt_data_nova = table.Column<DateOnly>(type: "date", nullable: false),
                    ds_renovado_por = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    dt_renovado_em = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ds_observacao = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renovacoes_de_vencimento", x => x.id_pk_renovacao_de_vencimento);
                    table.ForeignKey(
                        name: "fk_renovacoes_de_vencimento_item_vencimento",
                        column: x => x.id_fk_item_vencimento,
                        principalTable: "tb_itens_vencimento",
                        principalColumn: "id_pk_item_vencimento",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uk_avisos_de_vencimento_enviados_id_fk_item_vencimento_nr_ciclo_nr_marco",
                table: "tb_avisos_de_vencimento_enviados",
                columns: new[] { "id_fk_item_vencimento", "nr_ciclo", "nr_marco" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_itens_vencimento_id_fk_setor_dt_data_vencimento",
                table: "tb_itens_vencimento",
                columns: new[] { "id_fk_setor", "dt_data_vencimento" });

            migrationBuilder.CreateIndex(
                name: "idx_renovacoes_de_vencimento_id_fk_item_vencimento",
                table: "tb_renovacoes_de_vencimento",
                column: "id_fk_item_vencimento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_avisos_de_vencimento_enviados");

            migrationBuilder.DropTable(
                name: "tb_renovacoes_de_vencimento");

            migrationBuilder.DropTable(
                name: "tb_itens_vencimento");
        }
    }
}
