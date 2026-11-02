using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Bibliotecas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "id_fk_setor",
                table: "tb_documentos",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "id_fk_biblioteca",
                table: "tb_documentos",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tb_bibliotecas",
                columns: table => new
                {
                    id_pk_biblioteca = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ds_nome = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ds_slug = table.Column<string>(type: "nvarchar(53)", maxLength: 53, nullable: false),
                    ds_icone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    fl_ativo = table.Column<bool>(type: "bit", nullable: false),
                    dt_created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bibliotecas", x => x.id_pk_biblioteca);
                });

            migrationBuilder.CreateIndex(
                name: "idx_documentos_id_fk_biblioteca_id_fk_parent_nr_ordem",
                table: "tb_documentos",
                columns: new[] { "id_fk_biblioteca", "id_fk_parent", "nr_ordem" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_documentos_dono",
                table: "tb_documentos",
                sql: "(id_fk_setor IS NOT NULL AND id_fk_biblioteca IS NULL) OR (id_fk_setor IS NULL AND id_fk_biblioteca IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "uk_bibliotecas_ds_slug",
                table: "tb_bibliotecas",
                column: "ds_slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_documentos_biblioteca",
                table: "tb_documentos",
                column: "id_fk_biblioteca",
                principalTable: "tb_bibliotecas",
                principalColumn: "id_pk_biblioteca",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documentos_biblioteca",
                table: "tb_documentos");

            migrationBuilder.DropTable(
                name: "tb_bibliotecas");

            migrationBuilder.DropIndex(
                name: "idx_documentos_id_fk_biblioteca_id_fk_parent_nr_ordem",
                table: "tb_documentos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_documentos_dono",
                table: "tb_documentos");

            migrationBuilder.DropColumn(
                name: "id_fk_biblioteca",
                table: "tb_documentos");

            migrationBuilder.AlterColumn<Guid>(
                name: "id_fk_setor",
                table: "tb_documentos",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
