using AwesomeAssertions;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Web.Models.Documentos;
using Secco.Intranet.Web.Theming.Contracts;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>A montagem do modelo da lista de documentos: toda regra de exibição sai daqui.</summary>
public class ListaDeDocumentosFactoryTests
{
	private static readonly DateTimeOffset Quando = new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);

	private static DocumentoDto Arquivo(
		string titulo,
		Guid? id = null,
		Guid? parentId = null,
		string nome = "relatorio.pdf",
		long tamanho = 122_880,
		Visibilidade visibilidade = Visibilidade.Setor) =>
		new(
			id ?? Guid.NewGuid(),
			titulo,
			"Descrição",
			nome,
			"application/pdf",
			tamanho,
			visibilidade,
			Guid.NewGuid(),
			"Financeiro",
			"financeiro",
			"quem.criou",
			Quando,
			TipoDeDocumento.Arquivo,
			parentId);

	private static DocumentoDto Grupo(string titulo) =>
		new(
			Guid.NewGuid(),
			titulo,
			null,
			null,
			null,
			null,
			Visibilidade.Setor,
			Guid.NewGuid(),
			"Financeiro",
			"financeiro",
			"quem.criou",
			Quando,
			TipoDeDocumento.Grupo);

	private static string Url(Guid id) => $"/documentos/{id}/download";

	[Fact]
	public void Montar_GrupoVazio_FicaDeFora()
	{
		var vazio = new NoDeDocumentoDto(Grupo("Vazio"), []);
		var cheio = Grupo("Cheio");
		var filho = Arquivo("Filho");

		var modelo = ListaDeDocumentosFactory.Montar(
			[vazio, new NoDeDocumentoDto(cheio, [filho])], Url);

		modelo.Itens.Should().ContainSingle().Which.Titulo.Should().Be("Cheio");
	}

	[Fact]
	public void Montar_Grupo_SemUrlIconeNemBadge()
	{
		var modelo = ListaDeDocumentosFactory.Montar(
			[new NoDeDocumentoDto(Grupo("Políticas"), [Arquivo("Filho")])], Url);

		var grupo = modelo.Itens.Single();

		grupo.EhGrupo.Should().BeTrue();
		grupo.Url.Should().BeNull();
		grupo.Icone.Should().BeNull();
		grupo.Tipo.Should().BeNull();
		grupo.Detalhe.Should().BeNull();
		grupo.Badge.Should().BeNull();
	}

	[Fact]
	public void Montar_Filhos_NaOrdem()
	{
		var primeiro = Arquivo("Primeiro");
		var segundo = Arquivo("Segundo");

		var modelo = ListaDeDocumentosFactory.Montar(
			[new NoDeDocumentoDto(Grupo("Pasta"), [primeiro, segundo])], Url);

		modelo.Itens.Single().Filhos.Select(f => f.Titulo).Should().Equal("Primeiro", "Segundo");
		modelo.Itens.Single().Filhos.Should().OnlyContain(f => !f.EhGrupo && f.Filhos.Count == 0);
	}

	[Fact]
	public void Montar_Arquivo_DetalheEEtiqueta()
	{
		var arquivo = Arquivo("Relatório");

		var item = ListaDeDocumentosFactory.Montar([new NoDeDocumentoDto(arquivo, [])], Url).Itens.Single();

		item.EhGrupo.Should().BeFalse();
		item.Url.Should().Be(Url(arquivo.Id));
		item.Icone.Should().Be("bi-file-earmark-pdf");
		item.Tipo.Should().Be("PDF");
		item.Detalhe.Should().Be($"120 KB · {Quando.ToLocalTime():dd/MM/yyyy}");
		item.Badge.Should().Be(new BadgeModel("Financeiro", BadgeVariante.Setor, "financeiro"));
	}

	[Fact]
	public void Montar_ArquivoDaEmpresa_BadgeNeutro()
	{
		var item = ListaDeDocumentosFactory
			.Montar([new NoDeDocumentoDto(Arquivo("Geral", visibilidade: Visibilidade.Empresa), [])], Url)
			.Itens.Single();

		item.Badge.Should().Be(new BadgeModel("Toda a empresa", BadgeVariante.Neutro));
	}

	[Theory]
	[InlineData(512, "512 B")]
	[InlineData(3_145_728, "3 MB")]
	public void Montar_Tamanho_UsaAsFaixasDeSempre(long bytes, string esperado)
	{
		var item = ListaDeDocumentosFactory
			.Montar([new NoDeDocumentoDto(Arquivo("X", tamanho: bytes), [])], Url)
			.Itens.Single();

		item.Detalhe.Should().StartWith(esperado);
	}

	[Fact]
	public void Etiqueta_ExtensaoEmMaiusculas()
	{
		TiposDeArquivo.Etiqueta("relatorio.pdf").Should().Be("PDF");
		TiposDeArquivo.Etiqueta("Planilha.XlSx").Should().Be("XLSX");
	}

	[Fact]
	public void Etiqueta_SemExtensao_Nulo()
	{
		TiposDeArquivo.Etiqueta("LEIAME").Should().BeNull();
		TiposDeArquivo.Etiqueta(null).Should().BeNull();
	}
}
