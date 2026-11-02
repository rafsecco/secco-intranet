using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Obter para edição e editar documento (metadados e troca de arquivo).</summary>
public class EditarDocumentoHandlerTests
{
	private static readonly byte[] Pdf = [.. "%PDF-1.7 conteudo"u8];

	private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class Cenario
	{
		public Cenario()
		{
			Setor = new Setor("Recursos Humanos", "rh");
			Repositorio.RegistrarSetor(Setor);
			Store.ContadorDeSalvamentos = () => Repositorio.Gravacoes;
			Documento = Novo("Antigo");
			Editar = new EditarDocumentoHandler(
				Repositorio,
				Store,
				Opcoes,
				new IntranetOptions(),
				Trilha,
				NullLogger<EditarDocumentoHandler>.Instance);
			Obter = new ObterDocumentoParaEdicaoHandler(Repositorio);
		}

		public Setor Setor { get; }

		public DocumentoRepositorioEmMemoria Repositorio { get; } = new();

		public ArquivoStoreFalso Store { get; } = new();

		public TrilhaFalsa Trilha { get; } = new();

		public DocumentoOptions Opcoes { get; } = new();

		public Documento Documento { get; }

		public EditarDocumentoHandler Editar { get; }

		public ObterDocumentoParaEdicaoHandler Obter { get; }

		public Documento Novo(string titulo, string? descricao = null)
		{
			var documento = new Documento(
				DonoDeDocumento.DoSetor(Setor.Id),
				titulo,
				descricao,
				"a.pdf",
				"application/pdf",
				10,
				Visibilidade.Setor,
				"docs/antigo.bin",
				"secco-enc:v1:x",
				"alguem");
			Repositorio.Documentos.Add(documento);

			return documento;
		}

		public EditarDocumentoCommand Comando(
			Documento? alvo = null,
			string? titulo = "Antigo",
			string? descricao = null,
			Visibilidade visibilidade = Visibilidade.Setor,
			string? nomeArquivo = null,
			long tamanho = 0,
			Stream? conteudo = null,
			string setorSlug = "rh",
			IReadOnlySet<ReferenciaDeDono>? administrados = null,
			bool exigirVinculo = true) =>
			new(
				(alvo ?? Documento).Id,
				ReferenciaDeDono.DoSetor(setorSlug),
				titulo,
				descricao,
				visibilidade,
				nomeArquivo,
				tamanho,
				conteudo,
				administrados ?? new HashSet<ReferenciaDeDono> { ReferenciaDeDono.DoSetor("rh") },
				exigirVinculo);
	}

	private static MemoryStream Fluxo(byte[] bytes) => new(bytes);

	[Fact]
	public async Task Editar_SoMetadados_NaoGravaNemRemoveArquivo()
	{
		var c = new Cenario();

		var resultado = await c.Editar.HandleAsync(
			c.Comando(titulo: "Novo", visibilidade: Visibilidade.Empresa),
			CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		c.Store.Gravados.Should().BeEmpty();
		c.Store.Removidos.Should().BeEmpty();
		c.Documento.Titulo.Should().Be("Novo");
		c.Documento.Visibilidade.Should().Be(Visibilidade.Empresa);
		c.Documento.CaminhoRelativo.Should().Be("docs/antigo.bin");
	}

	[Fact]
	public async Task Editar_ComArquivoNovo_GravaSalvaEDepoisRemoveOAntigo()
	{
		var c = new Cenario();
		using var fluxo = Fluxo(Pdf);

		var resultado = await c.Editar.HandleAsync(
			c.Comando(nomeArquivo: "novo.pdf", tamanho: Pdf.Length, conteudo: fluxo),
			CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		c.Store.Gravados.Should().ContainSingle();
		c.Documento.CaminhoRelativo.Should().Be(c.Store.Gravados[0].Caminho);
		c.Documento.NomeArquivo.Should().Be("novo.pdf");
		c.Store.Removidos.Should().Equal("docs/antigo.bin");
		c.Store.SalvamentosNasRemocoes.Should().Equal(c.Repositorio.Gravacoes);
		c.Repositorio.Gravacoes.Should().Be(1);
	}

	[Fact]
	public async Task Editar_FalhaAoRemoverAntigo_EdicaoValeERegistraAviso()
	{
		var c = new Cenario();
		c.Store.FalharAoRemover = true;
		using var fluxo = Fluxo(Pdf);

		var resultado = await c.Editar.HandleAsync(
			c.Comando(nomeArquivo: "novo.pdf", tamanho: Pdf.Length, conteudo: fluxo),
			CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		c.Documento.CaminhoRelativo.Should().Be(c.Store.Gravados[0].Caminho);
		c.Trilha.Registros.Should().ContainSingle();
	}

	[Fact]
	public async Task Editar_ArquivoNaoAceito_NaoGravaNemAltera()
	{
		var c = new Cenario();
		using var fluxo = Fluxo(Png);

		var resultado = await c.Editar.HandleAsync(
			c.Comando(titulo: "Outro", nomeArquivo: "falso.pdf", tamanho: Png.Length, conteudo: fluxo),
			CancellationToken.None);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Documentos.ArquivoNaoAceito);
		c.Store.Gravados.Should().BeEmpty();
		c.Documento.Titulo.Should().Be("Antigo");
		c.Repositorio.Gravacoes.Should().Be(0);
	}

	[Fact]
	public async Task Editar_ArquivoMaiorQueOLimite_Recusa()
	{
		var c = new Cenario();
		using var fluxo = Fluxo(Pdf);

		var resultado = await c.Editar.HandleAsync(
			c.Comando(nomeArquivo: "novo.pdf", tamanho: c.Opcoes.TamanhoMaximoBytes + 1, conteudo: fluxo),
			CancellationToken.None);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Documentos.ArquivoMuitoGrande(c.Opcoes.TamanhoMaximoBytes));
		c.Store.Gravados.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_NomeComCaminho_GuardaSoONome()
	{
		var c = new Cenario();
		using var fluxo = Fluxo(Pdf);

		var resultado = await c.Editar.HandleAsync(
			c.Comando(nomeArquivo: "C:\\fakepath\\nova.pdf", tamanho: Pdf.Length, conteudo: fluxo),
			CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		c.Documento.NomeArquivo.Should().Be("nova.pdf");
	}

	[Theory]
	[InlineData("C:\\fakepath\\nova.pdf")]
	[InlineData("../dir/nova.pdf")]
	[InlineData("nova.pdf")]
	public void NomeSemCaminho_DevolveSoONome(string entrada) =>
		TiposDeArquivo.NomeSemCaminho(entrada).Should().Be("nova.pdf");

	[Fact]
	public async Task Editar_GrupoComArquivo_RecusaArquivoEmGrupo()
	{
		var c = new Cenario();
		var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(c.Setor.Id), "Grupo", null, "alguem");
		c.Repositorio.Documentos.Add(grupo);
		using var fluxo = Fluxo(Pdf);

		var resultado = await c.Editar.HandleAsync(
			c.Comando(grupo, titulo: "Grupo", nomeArquivo: "novo.pdf", tamanho: Pdf.Length, conteudo: fluxo),
			CancellationToken.None);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Documentos.ArquivoEmGrupo);
		c.Store.Gravados.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_GrupoComVisibilidadeEmpresa_IgnoraEMantemSetor()
	{
		var c = new Cenario();
		var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(c.Setor.Id), "Grupo", null, "alguem");
		c.Repositorio.Documentos.Add(grupo);

		var resultado = await c.Editar.HandleAsync(
			c.Comando(grupo, titulo: "Grupo novo", visibilidade: Visibilidade.Empresa),
			CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		grupo.Titulo.Should().Be("Grupo novo");
		grupo.Visibilidade.Should().Be(Visibilidade.Setor);
	}

	[Fact]
	public async Task Editar_DescricaoAcimaDe4096_RecusaDescricaoTooLong()
	{
		var c = new Cenario();

		var resultado = await c.Editar.HandleAsync(
			c.Comando(descricao: new string('x', 4097)),
			CancellationToken.None);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Documentos.DescricaoTooLong(4096));
	}

	[Theory]
	[InlineData("semPermissao")]
	[InlineData("arquivado")]
	[InlineData("inexistente")]
	[InlineData("outroSetor")]
	public async Task Editar_SemPermissao_Arquivado_Inexistente_OuDeOutroSetor_DevolveNotFound(string caso)
	{
		var c = new Cenario();
		var comando = c.Comando(titulo: "Novo");

		switch (caso)
		{
			case "semPermissao":
				comando = c.Comando(titulo: "Novo", administrados: new HashSet<ReferenciaDeDono>());
				break;
			case "arquivado":
				c.Documento.Arquivar();
				break;
			case "inexistente":
				comando = comando with { DocumentoId = Guid.NewGuid() };
				break;
			case "outroSetor":
				comando = c.Comando(titulo: "Novo", setorSlug: "ti", administrados: new HashSet<ReferenciaDeDono> { ReferenciaDeDono.DoSetor("rh"), ReferenciaDeDono.DoSetor("ti") });
				break;
		}

		var resultado = await c.Editar.HandleAsync(comando, CancellationToken.None);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Documentos.NotFound);
		c.Documento.Titulo.Should().Be("Antigo");
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_SemNenhumaMudanca_NaoSalvaNemRegistra()
	{
		var c = new Cenario();

		var resultado = await c.Editar.HandleAsync(c.Comando(), CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Should().Be(c.Documento.Id);
		c.Repositorio.Gravacoes.Should().Be(0);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_RegistraTrilhaSoComOQueMudou()
	{
		var c = new Cenario();
		using var fluxo = Fluxo(Encoding.UTF8.GetBytes("PK\u0003\u0004"));

		var resultado = await c.Editar.HandleAsync(
			c.Comando(
				titulo: "Novo",
				visibilidade: Visibilidade.Empresa,
				nomeArquivo: "b.docx",
				tamanho: 3,
				conteudo: fluxo),
			CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		var registro = c.Trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DocumentoEditar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Documento);
		registro.RecursoId.Should().Be(c.Documento.Id.ToString());
		registro.Metadata.Should().Be(
			"{\"setor\":\"rh\",\"titulo\":{\"de\":\"Antigo\",\"para\":\"Novo\"},"
			+ "\"visibilidade\":{\"de\":\"Setor\",\"para\":\"Empresa\"},"
			+ "\"arquivo\":{\"de\":{\"nome\":\"a.pdf\",\"tamanho\":10},\"para\":{\"nome\":\"b.docx\",\"tamanho\":3}}}");
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.TryGetProperty("descricaoAlterada", out _).Should().BeFalse();
	}

	[Fact]
	public async Task Editar_DescricaoMudou_RegistraDescricaoAlterada()
	{
		var c = new Cenario();

		await c.Editar.HandleAsync(c.Comando(descricao: "algo"), CancellationToken.None);

		c.Trilha.Registros.Should().ContainSingle().Which.Metadata.Should().Be("{\"setor\":\"rh\",\"descricaoAlterada\":true}");
	}

	[Fact]
	public async Task Obter_DevolveOsDadosAtuais()
	{
		var c = new Cenario();
		var arquivo = c.Novo("Contrato", "texto");

		var resultado = await c.Obter.HandleAsync(
			new ObterDocumentoParaEdicaoQuery(arquivo.Id, ReferenciaDeDono.DoSetor("rh"), new HashSet<ReferenciaDeDono> { ReferenciaDeDono.DoSetor("rh") }, true),
			CancellationToken.None);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Should().Be(new DocumentoParaEdicaoDto(
			arquivo.Id, false, "Contrato", "texto", Visibilidade.Setor, "a.pdf"));
	}

	[Fact]
	public async Task Obter_ForaDoAlcance_DevolveNotFound()
	{
		var c = new Cenario();

		var resultado = await c.Obter.HandleAsync(
			new ObterDocumentoParaEdicaoQuery(c.Documento.Id, ReferenciaDeDono.DoSetor("rh"), new HashSet<ReferenciaDeDono>(), true),
			CancellationToken.None);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Documentos.NotFound);
	}
}
