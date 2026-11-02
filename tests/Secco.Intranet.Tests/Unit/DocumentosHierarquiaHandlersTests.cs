using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Árvore de documentos, publicação abaixo de um item e criação de grupo.</summary>
public class DocumentosHierarquiaHandlersTests
{
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
		public Cenario()
		{
			Setor = new Setor("Financeiro", "financeiro");
			Repositorio.RegistrarSetor(Setor);
			Publicar = new PublicarDocumentoHandler(
				Repositorio,
				new ResolvedorDeDono(new SetorRepositorioFalso(Setor), new BibliotecaRepositorioFalso()),
				Store,
				new DocumentoOptions(),
				new IntranetOptions(),
				Trilha);
			CriarGrupo = new CriarGrupoDeDocumentosHandler(
				Repositorio,
				new ResolvedorDeDono(new SetorRepositorioFalso(Setor), new BibliotecaRepositorioFalso()),
				new IntranetOptions(),
				Trilha);
			Listar = new ListarDocumentosHandler(Repositorio);
		}

		public Setor Setor { get; }

		public DocumentoRepositorioEmMemoria Repositorio { get; } = new();

		public StoreContador Store { get; } = new();

		public TrilhaFalsa Trilha { get; } = new();

		public PublicarDocumentoHandler Publicar { get; }

		public CriarGrupoDeDocumentosHandler CriarGrupo { get; }

		public ListarDocumentosHandler Listar { get; }

		public Documento Arquivo(string titulo, Guid? parentId = null, int ordem = 0, Setor? setor = null)
		{
			var documento = new Documento(
				DonoDeDocumento.DoSetor((setor ?? Setor).Id),
				titulo,
				null,
				"a.pdf",
				"application/pdf",
				10,
				Visibilidade.Setor,
				"t/ab/x",
				"secco-enc:v1:x",
				"alguem",
				parentId,
				ordem);
			Repositorio.Documentos.Add(documento);

			return documento;
		}

		public Documento Grupo(string titulo, int ordem = 0)
		{
			var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(Setor.Id), titulo, null, "alguem");
			grupo.Posicionar(null, ordem);
			Repositorio.Documentos.Add(grupo);

			return grupo;
		}

		public Task<Result<DocumentoDto>> PublicarAsync(string titulo, Guid? parentId = null)
		{
			var conteudo = new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34]);

			return Publicar.HandleAsync(new PublicarDocumentoCommand(
				ReferenciaDeDono.DoSetor("financeiro"),
				titulo,
				null,
				"a.pdf",
				conteudo.Length,
				conteudo,
				Visibilidade.Setor,
				"quem.publicou",
				parentId));
		}

		public int OrdemDe(Guid id) => Repositorio.Documentos.Single(item => item.Id == id).Ordem;
	}

	[Fact]
	public async Task Publicar_NaRaiz_EntraNoTopo()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);
		var b = cenario.Arquivo("B", ordem: 1);

		var resultado = await cenario.PublicarAsync("Novo");

		resultado.IsSuccess.Should().BeTrue();
		cenario.OrdemDe(resultado.Value.Id).Should().Be(0);
		cenario.OrdemDe(a.Id).Should().Be(1);
		cenario.OrdemDe(b.Id).Should().Be(2);
		resultado.Value.Ordem.Should().Be(0);
		resultado.Value.ParentId.Should().BeNull();
		cenario.Repositorio.Gravacoes.Should().Be(1, "a renumeração e o item novo saem em uma única gravação");
	}

	[Fact]
	public async Task Publicar_AbaixoDeItem_EntraNoFimDosFilhos()
	{
		var cenario = new Cenario();
		var pai = cenario.Arquivo("Pai");
		var filho0 = cenario.Arquivo("F0", pai.Id, 0);
		var filho1 = cenario.Arquivo("F1", pai.Id, 1);

		var resultado = await cenario.PublicarAsync("Novo", pai.Id);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.ParentId.Should().Be(pai.Id);
		cenario.OrdemDe(resultado.Value.Id).Should().Be(2);
		cenario.OrdemDe(filho0.Id).Should().Be(0);
		cenario.OrdemDe(filho1.Id).Should().Be(1);
	}

	[Fact]
	public async Task Publicar_AbaixoDeGrupo_Aceita()
	{
		var cenario = new Cenario();
		var grupo = cenario.Grupo("Políticas");

		var resultado = await cenario.PublicarAsync("Novo", grupo.Id);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.ParentId.Should().Be(grupo.Id);
		resultado.Value.Ordem.Should().Be(0);
	}

	[Fact]
	public async Task Publicar_PaiArquivado_RecusaSemGravarArquivo()
	{
		var cenario = new Cenario();
		var pai = cenario.Arquivo("Pai");
		pai.Arquivar();

		var resultado = await cenario.PublicarAsync("Novo", pai.Id);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Documentos.ParentInvalido);
		cenario.Store.Gravacoes.Should().Be(0);
		cenario.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Publicar_PaiInexistente_Recusa()
	{
		var cenario = new Cenario();

		var resultado = await cenario.PublicarAsync("Novo", Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Documentos.ParentInvalido);
		cenario.Store.Gravacoes.Should().Be(0);
	}

	[Fact]
	public async Task Publicar_PaiQueEFilho_Recusa()
	{
		var cenario = new Cenario();
		var avo = cenario.Grupo("Grupo");
		var pai = cenario.Arquivo("Filho", avo.Id);

		var resultado = await cenario.PublicarAsync("Neto", pai.Id);

		resultado.Error.Should().Be(IntranetErrors.Documentos.ParentInvalido);
		cenario.Store.Gravacoes.Should().Be(0);
	}

	[Fact]
	public async Task Publicar_PaiDeOutroSetor_Recusa()
	{
		var cenario = new Cenario();
		var outro = new Setor("RH", "rh");
		cenario.Repositorio.RegistrarSetor(outro);
		var pai = cenario.Arquivo("Pai do RH", setor: outro);

		var resultado = await cenario.PublicarAsync("Novo", pai.Id);

		resultado.Error.Should().Be(IntranetErrors.Documentos.ParentInvalido);
		cenario.Store.Gravacoes.Should().Be(0);
	}

	[Fact]
	public async Task Publicar_AuditoriaLevaPai()
	{
		var cenario = new Cenario();
		var pai = cenario.Grupo("Grupo");

		var abaixo = await cenario.PublicarAsync("Abaixo", pai.Id);
		var naRaiz = await cenario.PublicarAsync("Raiz");

		var registros = cenario.Trilha.Registros;
		registros.Should().HaveCount(2);

		var payloadAbaixo = JsonDocument.Parse(registros[0].Metadata!).RootElement;
		payloadAbaixo.GetProperty("pai").GetString().Should().Be(pai.Id.ToString());
		payloadAbaixo.GetProperty("arquivo").GetString().Should().Be("a.pdf");
		registros[0].RecursoId.Should().Be(abaixo.Value.Id.ToString());

		var payloadRaiz = JsonDocument.Parse(registros[1].Metadata!).RootElement;
		payloadRaiz.GetProperty("pai").ValueKind.Should().Be(JsonValueKind.Null);
		registros[1].RecursoId.Should().Be(naRaiz.Value.Id.ToString());
	}

	[Fact]
	public async Task CriarGrupo_EntraNoTopoDaRaizEAudita()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);

		var resultado = await cenario.CriarGrupo.HandleAsync(
			new CriarGrupoDeDocumentosCommand(ReferenciaDeDono.DoSetor("financeiro"), "Políticas", "Regras", "quem.criou"));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Tipo.Should().Be(TipoDeDocumento.Grupo);
		resultado.Value.Titulo.Should().Be("Políticas");
		resultado.Value.NomeArquivo.Should().BeNull();
		resultado.Value.Ordem.Should().Be(0);
		cenario.OrdemDe(a.Id).Should().Be(1);

		var registro = cenario.Trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DocumentoGrupoCriar);
		registro.Verbo.Should().Be("documento.grupo-criar");
		registro.Recurso.Should().Be(RecursosDeAuditoria.Documento);
		registro.RecursoId.Should().Be(resultado.Value.Id.ToString());
		var payload = JsonDocument.Parse(registro.Metadata!).RootElement;
		payload.GetProperty("setor").GetString().Should().Be("financeiro");
		payload.GetProperty("titulo").GetString().Should().Be("Políticas");
	}

	[Fact]
	public async Task CriarGrupo_TituloVazioOuLongo_Recusa()
	{
		var cenario = new Cenario();
		var limite = new IntranetOptions().MaxNameLength;

		var vazio = await cenario.CriarGrupo.HandleAsync(
			new CriarGrupoDeDocumentosCommand(ReferenciaDeDono.DoSetor("financeiro"), "  ", null, "x"));
		var longo = await cenario.CriarGrupo.HandleAsync(
			new CriarGrupoDeDocumentosCommand(ReferenciaDeDono.DoSetor("financeiro"), new string('a', limite + 1), null, "x"));

		vazio.Error.Should().Be(IntranetErrors.Documentos.TituloRequired);
		longo.Error.Should().Be(IntranetErrors.Documentos.TituloTooLong(limite));
		cenario.Repositorio.Documentos.Should().BeEmpty();
		cenario.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task CriarGrupo_SetorInexistente_Recusa()
	{
		var cenario = new Cenario();

		var resultado = await cenario.CriarGrupo.HandleAsync(
			new CriarGrupoDeDocumentosCommand(ReferenciaDeDono.DoSetor("nao-existe"), "Grupo", null, "x"));

		resultado.Error.Should().Be(IntranetErrors.Setores.NotFound);
	}

	[Fact]
	public async Task Listar_MontaArvore_RaizEFilhosNaOrdem()
	{
		var cenario = new Cenario();
		var segundo = cenario.Arquivo("Segundo", ordem: 1);
		var grupo = cenario.Grupo("Grupo", ordem: 0);
		cenario.Arquivo("Filho B", grupo.Id, 1);
		cenario.Arquivo("Filho A", grupo.Id, 0);
		var arquivado = cenario.Arquivo("Arquivado", ordem: 2);
		arquivado.Arquivar();
		var orfao = cenario.Arquivo("Pai arquivado", ordem: 3);
		orfao.Arquivar();
		cenario.Arquivo("Filho do arquivado", orfao.Id, 0);

		var resultado = await cenario.Listar.HandleAsync(new ListarDocumentosQuery(ReferenciaDeDono.DoSetor("financeiro")));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Select(no => no.Documento.Titulo).Should().Equal("Grupo", "Segundo");
		resultado.Value[0].Filhos.Select(filho => filho.Titulo).Should().Equal("Filho A", "Filho B");
		resultado.Value[1].Documento.Id.Should().Be(segundo.Id);
		resultado.Value[1].Filhos.Should().BeEmpty();
	}

	[Fact]
	public async Task Listar_GrupoVazio_ContinuaNoResultado()
	{
		var cenario = new Cenario();
		cenario.Grupo("Vazio");

		var resultado = await cenario.Listar.HandleAsync(new ListarDocumentosQuery(ReferenciaDeDono.DoSetor("financeiro")));

		var no = resultado.Value.Should().ContainSingle().Subject;
		no.Documento.Tipo.Should().Be(TipoDeDocumento.Grupo);
		no.Filhos.Should().BeEmpty();
	}
}
