using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Secco.Intranet.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DocumentosHierarquia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_documentos_id_fk_setor",
                table: "tb_documentos");

            migrationBuilder.AlterColumn<long>(
                name: "nr_tamanho",
                table: "tb_documentos",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "ds_nome_arquivo",
                table: "tb_documentos",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "ds_content_type",
                table: "tb_documentos",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ds_chave_embrulhada",
                table: "tb_documentos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "ds_caminho_relativo",
                table: "tb_documentos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<Guid>(
                name: "id_fk_parent",
                table: "tb_documentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ie_tipo",
                table: "tb_documentos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "nr_ordem",
                table: "tb_documentos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "idx_documentos_id_fk_parent",
                table: "tb_documentos",
                column: "id_fk_parent");

            migrationBuilder.CreateIndex(
                name: "idx_documentos_id_fk_setor_id_fk_parent_nr_ordem",
                table: "tb_documentos",
                columns: new[] { "id_fk_setor", "id_fk_parent", "nr_ordem" });

            migrationBuilder.AddForeignKey(
                name: "fk_documentos_documento",
                table: "tb_documentos",
                column: "id_fk_parent",
                principalTable: "tb_documentos",
                principalColumn: "id_pk_documento",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documentos_documento",
                table: "tb_documentos");

            migrationBuilder.DropIndex(
                name: "idx_documentos_id_fk_parent",
                table: "tb_documentos");

            migrationBuilder.DropIndex(
                name: "idx_documentos_id_fk_setor_id_fk_parent_nr_ordem",
                table: "tb_documentos");

            migrationBuilder.DropColumn(
                name: "id_fk_parent",
                table: "tb_documentos");

            migrationBuilder.DropColumn(
                name: "ie_tipo",
                table: "tb_documentos");

            migrationBuilder.DropColumn(
                name: "nr_ordem",
                table: "tb_documentos");

            migrationBuilder.AlterColumn<long>(
                name: "nr_tamanho",
                table: "tb_documentos",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ds_nome_arquivo",
                table: "tb_documentos",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ds_content_type",
                table: "tb_documentos",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ds_chave_embrulhada",
                table: "tb_documentos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ds_caminho_relativo",
                table: "tb_documentos",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_documentos_id_fk_setor",
                table: "tb_documentos",
                column: "id_fk_setor");
        }
    }
}
