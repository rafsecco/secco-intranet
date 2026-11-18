using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class SalasEReservas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tb_exclusoes_de_evento_pendentes",
                columns: table => new
                {
                    id_pk_exclusao_de_evento_pendente = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_evento_externo_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    nr_tentativas = table.Column<int>(type: "integer", nullable: false),
                    dt_proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ds_motivo_da_falha = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fl_falhou = table.Column<bool>(type: "boolean", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exclusoes_de_evento_pendentes", x => x.id_pk_exclusao_de_evento_pendente);
                });

            migrationBuilder.CreateTable(
                name: "tb_salas",
                columns: table => new
                {
                    id_pk_sala = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ds_localizacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    nr_capacidade = table.Column<int>(type: "integer", nullable: true),
                    ds_recursos = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ds_email_na_agenda = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    nr_ordem = table.Column<int>(type: "integer", nullable: false),
                    fl_ativa = table.Column<bool>(type: "boolean", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salas", x => x.id_pk_sala);
                });

            migrationBuilder.CreateTable(
                name: "tb_reservas",
                columns: table => new
                {
                    id_pk_reserva = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_sala = table.Column<Guid>(type: "uuid", nullable: false),
                    dt_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dt_fim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ds_titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ds_descricao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reservado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_reservado_por_nome = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ds_reservado_por_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    nr_versao = table.Column<int>(type: "integer", nullable: false),
                    ie_espelhamento = table.Column<int>(type: "integer", nullable: false),
                    ds_evento_externo_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    nr_tentativas_de_espelhamento = table.Column<int>(type: "integer", nullable: false),
                    dt_proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ds_motivo_da_falha = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dt_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservas", x => x.id_pk_reserva);
                    table.ForeignKey(
                        name: "fk_reservas_sala",
                        column: x => x.id_fk_sala,
                        principalTable: "tb_salas",
                        principalColumn: "id_pk_sala",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tb_convidados_das_reservas",
                columns: table => new
                {
                    id_pk_convidado_da_reserva = table.Column<Guid>(type: "uuid", nullable: false),
                    id_fk_reserva = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ds_nome = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ds_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_convidados_das_reservas", x => x.id_pk_convidado_da_reserva);
                    table.ForeignKey(
                        name: "fk_convidados_das_reservas_reserva",
                        column: x => x.id_fk_reserva,
                        principalTable: "tb_reservas",
                        principalColumn: "id_pk_reserva",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_convidados_das_reservas_id_fk_reserva",
                table: "tb_convidados_das_reservas",
                column: "id_fk_reserva");

            migrationBuilder.CreateIndex(
                name: "idx_convidados_das_reservas_usuario_id",
                table: "tb_convidados_das_reservas",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "idx_exclusoes_de_evento_pendentes_fl_falhou_dt_proxima_tentativa_em",
                table: "tb_exclusoes_de_evento_pendentes",
                columns: new[] { "fl_falhou", "dt_proxima_tentativa_em" });

            migrationBuilder.CreateIndex(
                name: "idx_reservas_id_fk_sala_dt_inicio",
                table: "tb_reservas",
                columns: new[] { "id_fk_sala", "dt_inicio" });

            migrationBuilder.CreateIndex(
                name: "idx_reservas_ie_espelhamento_dt_proxima_tentativa_em",
                table: "tb_reservas",
                columns: new[] { "ie_espelhamento", "dt_proxima_tentativa_em" });

            migrationBuilder.CreateIndex(
                name: "uk_salas_ds_nome",
                table: "tb_salas",
                column: "ds_nome",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tb_convidados_das_reservas");

            migrationBuilder.DropTable(
                name: "tb_exclusoes_de_evento_pendentes");

            migrationBuilder.DropTable(
                name: "tb_reservas");

            migrationBuilder.DropTable(
                name: "tb_salas");
        }
    }
}
