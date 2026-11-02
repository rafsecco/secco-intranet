using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Os casos de uso de Documentos servindo uma biblioteca como dono.</summary>
public class DocumentosDeBibliotecaHandlersTests
{
	private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];

	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class StoreContador : IArquivoStore
	{
		public int Gravacoes { get; private set; }

		public Task<ArquivoGravado> GravarAsync(Stream conteudo, CancellationToken cancellationToken = default)
		{
			Gravacoes++;

			return Task.FromResult(new ArquivoGravado("t/ab/arquivo", "secco-enc:v1:x", 10));
		}

		public Task<ArquivoGravado> GravarAsync(
			Stream conteudo,
			string pastaLogica,
			CancellationToken cancellationToken = default) =>
			GravarAsync(conteudo, cancellationToken);

		public Task EscreverEmAsync(
			string caminhoRelativo,
			string chaveEmbrulhada,
			Stream destino,
			CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task RemoverAsync(string caminhoRelativo, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class SetorRepositorioFalso(Setor setor) : ISetorRepository
	{
		public Task AddAsync(Setor novo, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(setor);

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(setor);

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(string.Equals(slug, setor.Slug, StringComparison.OrdinalIgnoreCase) ? setor : null);

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(false);

		public Task<PagedResult<Setor>> SearchAsync(
			SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<Setor>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class Cenario
	{
		public Cenario(bool bibliotecaAtiva = true)
		{
			Setor = new Setor("TI", "ti");
			Biblioteca = new Biblioteca("Manuais", "ti");

			if (!bibliotecaAtiva)
			{
				Biblioteca.Desativar();
			}

			Repositorio.RegistrarSetor(Setor);
			Repositorio.RegistrarBiblioteca(Biblioteca);

			Publicar = new PublicarDocumentoHandler(
				Repositorio,
				new ResolvedorDeDono(new SetorRepositorioFalso(Setor), new BibliotecaRepositorioFalso(Biblioteca)),
				Store,
				new DocumentoOptions(),
				new IntranetOptions(),
				Trilha);
			Mover = new MoverDocumentoHandler(Repositorio, Trilha);
			Baixar = new BaixarDocumentoHandler(Repositorio, Store);
		}

		public Setor Setor { get; }

		public Biblioteca Biblioteca { get; }

		public DocumentoRepositorioEmMemoria Repositorio { get; } = new();

		public StoreContador Store { get; } = new();

		public TrilhaFalsa Trilha { get; } = new();

		public PublicarDocumentoHandler Publicar { get; }

		public MoverDocumentoHandler Mover { get; }

		public BaixarDocumentoHandler Baixar { get; }

		public Documento ArquivoDaBiblioteca(string titulo, int ordem = 0) =>
			Adicionar(DonoDeDocumento.DaBiblioteca(Biblioteca.Id), titulo, ordem);

		public Documento ArquivoDoSetor(string titulo, int ordem = 0) =>
			Adicionar(DonoDeDocumento.DoSetor(Setor.Id), titulo, ordem);

		private Documento Adicionar(DonoDeDocumento dono, string titulo, int ordem)
		{
			var documento = new Documento(
				dono,
				titulo,
				null,
				"a.pdf",
				"application/pdf",
				10,
				Visibilidade.Setor,
				"t/ab/x",
				"secco-enc:v1:x",
				"alguem",
				parentId: null,
				ordem);
			Repositorio.Documentos.Add(documento);

			return documento;
		}

		public Task<Result<DocumentoDto>> PublicarAsync(
			ReferenciaDeDono dono,
			Visibilidade visibilidade = Visibilidade.Setor,
			Guid? parentId = null)
		{
			var conteudo = new MemoryStream(Pdf);

			return Publicar.HandleAsync(new PublicarDocumentoCommand(
				dono,
				"Manual",
				null,
				"a.pdf",
				conteudo.Length,
				conteudo,
				visibilidade,
				"quem.publicou",
				parentId));
		}
	}

	private static HashSet<ReferenciaDeDono> Donos(params ReferenciaDeDono[] donos) => [.. donos];

	[Fact]
	public async Task Publicar_na_biblioteca_grava_dono_e_visibilidade_setor()
	{
		var cenario = new Cenario();

		var resultado = await cenario.PublicarAsync(ReferenciaDeDono.DaBiblioteca("ti"), Visibilidade.Empresa);

		resultado.IsSuccess.Should().BeTrue();
		var documento = cenario.Repositorio.Documentos.Should().ContainSingle().Subject;
		documento.BibliotecaId.Should().Be(cenario.Biblioteca.Id);
		documento.SetorId.Should().BeNull();
		documento.Visibilidade.Should().Be(Visibilidade.Setor);
		resultado.Value.Dono.Should().Be(ReferenciaDeDono.DaBiblioteca("ti"));
		resultado.Value.DonoNome.Should().Be("Manuais");
	}

	[Fact]
	public async Task Publicar_em_biblioteca_inativa_e_NotFound()
	{
		var cenario = new Cenario(bibliotecaAtiva: false);

		var resultado = await cenario.PublicarAsync(ReferenciaDeDono.DaBiblioteca("ti"));

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.NotFound);
		cenario.Repositorio.Documentos.Should().BeEmpty();
		cenario.Store.Gravacoes.Should().Be(0);
	}

	[Fact]
	public async Task Pai_de_setor_na_biblioteca_e_ParentInvalido()
	{
		var cenario = new Cenario();
		var pai = cenario.ArquivoDoSetor("Pai");

		var resultado = await cenario.PublicarAsync(ReferenciaDeDono.DaBiblioteca("ti"), parentId: pai.Id);

		resultado.Error.Should().Be(IntranetErrors.Documentos.ParentInvalido);
		cenario.Store.Gravacoes.Should().Be(0);
		cenario.Repositorio.Gravacoes.Should().Be(0);
	}

	[Fact]
	public async Task Pai_de_outra_biblioteca_e_ParentInvalido()
	{
		var cenario = new Cenario();
		var outra = new Biblioteca("Outra", "outra");
		cenario.Repositorio.RegistrarBiblioteca(outra);
		var pai = new Documento(
			DonoDeDocumento.DaBiblioteca(outra.Id),
			"Pai",
			null,
			"a.pdf",
			"application/pdf",
			10,
			Visibilidade.Setor,
			"t/ab/x",
			"secco-enc:v1:x",
			"alguem");
		cenario.Repositorio.Documentos.Add(pai);

		var resultado = await cenario.PublicarAsync(ReferenciaDeDono.DaBiblioteca("ti"), parentId: pai.Id);

		resultado.Error.Should().Be(IntranetErrors.Documentos.ParentInvalido);
		cenario.Store.Gravacoes.Should().Be(0);
		cenario.Repositorio.Gravacoes.Should().Be(0);
	}

	[Fact]
	public async Task Mover_documento_de_biblioteca_pela_referencia_de_setor_de_mesmo_slug_e_NotFound()
	{
		var cenario = new Cenario();
		var a = cenario.ArquivoDaBiblioteca("A", 0);
		cenario.ArquivoDaBiblioteca("B", 1);

		var pelaBiblioteca = await cenario.Mover.HandleAsync(
			new MoverDocumentoCommand(ReferenciaDeDono.DaBiblioteca("ti"), a.Id, MovimentoDeDocumento.Descer));
		var peloSetor = await cenario.Mover.HandleAsync(
			new MoverDocumentoCommand(ReferenciaDeDono.DoSetor("ti"), a.Id, MovimentoDeDocumento.Subir));

		pelaBiblioteca.IsSuccess.Should().BeTrue();
		peloSetor.Error.Should().Be(IntranetErrors.Documentos.NotFound);
	}

	[Fact]
	public async Task Baixar_documento_de_biblioteca_exige_a_biblioteca_em_DonosComLeitura()
	{
		var cenario = new Cenario();
		var documento = cenario.ArquivoDaBiblioteca("A");

		var soComSetor = await cenario.Baixar.HandleAsync(
			new BaixarDocumentoQuery(documento.Id, Donos(ReferenciaDeDono.DoSetor("ti")), ExigirVinculo: true));
		var comBiblioteca = await cenario.Baixar.HandleAsync(
			new BaixarDocumentoQuery(documento.Id, Donos(ReferenciaDeDono.DaBiblioteca("ti")), ExigirVinculo: true));

		soComSetor.Error.Should().Be(IntranetErrors.Documentos.NotFound);
		comBiblioteca.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task Baixar_documento_de_biblioteca_inativa_e_NotFound()
	{
		var cenario = new Cenario(bibliotecaAtiva: false);
		var documento = cenario.ArquivoDaBiblioteca("A");

		var resultado = await cenario.Baixar.HandleAsync(
			new BaixarDocumentoQuery(documento.Id, Donos(ReferenciaDeDono.DaBiblioteca("ti")), ExigirVinculo: true));

		resultado.Error.Should().Be(IntranetErrors.Documentos.NotFound);
	}

	[Fact]
	public async Task Trilha_de_documento_de_biblioteca_leva_chave_biblioteca_e_nao_setor()
	{
		var cenario = new Cenario();

		var resultado = await cenario.PublicarAsync(ReferenciaDeDono.DaBiblioteca("ti"));

		resultado.IsSuccess.Should().BeTrue();
		var registro = cenario.Trilha.Registros.Should().ContainSingle().Subject;
		var raiz = JsonDocument.Parse(registro.Metadata!).RootElement;
		raiz.GetProperty("biblioteca").GetString().Should().Be("ti");
		raiz.TryGetProperty("setor", out _).Should().BeFalse();
	}
}
