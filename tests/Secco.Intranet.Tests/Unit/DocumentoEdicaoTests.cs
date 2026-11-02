using AwesomeAssertions;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Edição de metadados, troca do arquivo e guarda de visibilidade do <see cref="Documento"/>.</summary>
public class DocumentoEdicaoTests
{
	private static Documento NovoArquivo() => new(
		DonoDeDocumento.DoSetor(Guid.NewGuid()),
		"Política",
		"Descrição",
		"politica.pdf",
		"application/pdf",
		128,
		Visibilidade.Setor,
		"caminho/antigo",
		"secco-enc:v1:antiga",
		"quem.criou",
		Guid.NewGuid(),
		4);

	[Fact]
	public void Editar_TrocaTituloEDescricao_ComTrim()
	{
		var documento = NovoArquivo();
		var parentId = documento.ParentId;
		var criadoEm = documento.CreatedAt;

		documento.Editar("  Novo  ", "  desc  ");

		documento.Titulo.Should().Be("Novo");
		documento.Descricao.Should().Be("desc");
		documento.ParentId.Should().Be(parentId);
		documento.Ordem.Should().Be(4);
		documento.CriadoPor.Should().Be("quem.criou");
		documento.CreatedAt.Should().Be(criadoEm);
	}

	[Fact]
	public void Editar_DescricaoEmBranco_ViraNula()
	{
		var documento = NovoArquivo();

		documento.Editar("Novo", "   ");

		documento.Descricao.Should().BeNull();
	}

	[Fact]
	public void Editar_TituloVazio_Lanca()
	{
		var documento = NovoArquivo();

		var editar = () => documento.Editar("  ", "desc");

		editar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void TrocarArquivo_TrocaOsCincoCampos_EDevolveOCaminhoAnterior()
	{
		var documento = NovoArquivo();

		var anterior = documento.TrocarArquivo("nova.docx", "application/msword", 999, "caminho/novo", "secco-enc:v1:nova");

		anterior.Should().Be("caminho/antigo");
		documento.NomeArquivo.Should().Be("nova.docx");
		documento.ContentType.Should().Be("application/msword");
		documento.Tamanho.Should().Be(999);
		documento.CaminhoRelativo.Should().Be("caminho/novo");
		documento.ChaveEmbrulhada.Should().Be("secco-enc:v1:nova");
	}

	[Theory]
	[InlineData(0, "caminho/novo")]
	[InlineData(-1, "caminho/novo")]
	[InlineData(10, " ")]
	public void TrocarArquivo_TamanhoZeroOuCaminhoVazio_Lanca(long tamanho, string caminho)
	{
		var documento = NovoArquivo();

		var trocar = () => documento.TrocarArquivo("nova.docx", "application/msword", tamanho, caminho, "secco-enc:v1:nova");

		trocar.Should().Throw<DomainInvariantException>();
		documento.CaminhoRelativo.Should().Be("caminho/antigo");
	}

	[Fact]
	public void TrocarArquivo_EmGrupo_Lanca()
	{
		var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(Guid.NewGuid()), "Políticas", null, "quem.criou");

		var trocar = () => grupo.TrocarArquivo("nova.docx", "application/msword", 10, "caminho/novo", "secco-enc:v1:nova");

		trocar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void AlterarVisibilidade_GrupoParaEmpresa_Lanca()
	{
		var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(Guid.NewGuid()), "Políticas", null, "quem.criou");

		var alterar = () => grupo.AlterarVisibilidade(Visibilidade.Empresa);

		alterar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void AlterarVisibilidade_GrupoParaSetor_NaoLanca()
	{
		var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(Guid.NewGuid()), "Políticas", null, "quem.criou");

		var alterar = () => grupo.AlterarVisibilidade(Visibilidade.Setor);

		alterar.Should().NotThrow();
	}
}
