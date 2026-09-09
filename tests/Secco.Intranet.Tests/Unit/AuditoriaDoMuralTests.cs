using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Os verbos do Mural. Um verbo errado não quebra nada hoje — some da busca daqui a um ano,
/// quando alguém precisar saber quem publicou o quê.
/// </summary>
public class AuditoriaDoMuralTests
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

	private sealed class DiretorioVazioFalso : IDiretorioDeUsuarios
	{
		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
			CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<UsuarioDoTenant>>([]);
	}

	private sealed class NotificadorMudo : INotificadorDeMensagens
	{
		public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class PublicacaoRepositorioFalso(PublicacaoComSetor? encontrada) : IPublicacaoRepository
	{
		public Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<PublicacaoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(encontrada);

		public Task<PagedResult<PublicacaoDto>> ListarNoMuralAsync(
			MuralCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Create<PublicacaoDto>([], criteria.Page, 0));

		public Task<IReadOnlyList<PublicacaoDto>> ListarDoSetorAsync(
			string setorSlug, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<PublicacaoDto>>([]);
	}

	private sealed class SetorRepositorioFalso : ISetorRepository
	{
		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(new Setor("Financeiro", "financeiro"));

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(true);

		public Task<PagedResult<Setor>> SearchAsync(
			SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<Setor>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private static PublicacaoComSetor Existente() =>
		new(new Publicacao(Guid.NewGuid(), "Comunicado", "Corpo secreto", TipoPublicacao.Aviso,
				Visibilidade.Empresa, PrioridadePublicacao.Normal,
				DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou"),
			"Financeiro", "financeiro");

	[Fact]
	public async Task Publicar_RegistraMuralPublicar()
	{
		var trilha = new TrilhaFalsa();
		var handler = new PublicarPublicacaoHandler(
			new PublicacaoRepositorioFalso(null), new SetorRepositorioFalso(), new IntranetOptions(),
			new DiretorioVazioFalso(), new NotificadorMudo(), new NotificacaoOptions(), trilha);

		var resultado = await handler.HandleAsync(new PublicarPublicacaoCommand(
			"financeiro", "Comunicado", "Corpo em **markdown**.", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal,
			DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou", Guid.NewGuid()));

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MuralPublicar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Publicacao);
		registro.RecursoId.Should().Be(resultado.Value.Publicacao.Id.ToString());
	}

	[Fact]
	public async Task Publicar_NaoLevaOCorpoNoMetadata()
	{
		var trilha = new TrilhaFalsa();
		var handler = new PublicarPublicacaoHandler(
			new PublicacaoRepositorioFalso(null), new SetorRepositorioFalso(), new IntranetOptions(),
			new DiretorioVazioFalso(), new NotificadorMudo(), new NotificacaoOptions(), trilha);

		await handler.HandleAsync(new PublicarPublicacaoCommand(
			"financeiro", "Comunicado", "Segredo industrial em texto longo.", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal,
			DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou", Guid.NewGuid()));

		var metadata = trilha.Registros.Should().ContainSingle().Subject.Metadata;
		metadata.Should().NotBeNull();
		metadata.Should().NotContain("Segredo industrial",
			"a trilha diz o que aconteceu; duplicar conteudo cria um segundo lugar de onde vazar");
		JsonDocument.Parse(metadata!).RootElement.GetProperty("titulo").GetString()
			.Should().Be("Comunicado");
	}

	[Fact]
	public async Task Editar_RegistraMuralEditar()
	{
		var trilha = new TrilhaFalsa();
		var encontrada = Existente();
		var handler = new EditarPublicacaoHandler(
			new PublicacaoRepositorioFalso(encontrada), new IntranetOptions(), trilha);

		await handler.HandleAsync(new EditarPublicacaoCommand(
			encontrada.Publicacao.Id, new HashSet<string>(["financeiro"], StringComparer.OrdinalIgnoreCase),
			ExigirVinculo: true, "Novo", "Corpo", TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, DateTimeOffset.UtcNow, null));

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MuralEditar);
		registro.RecursoId.Should().Be(encontrada.Publicacao.Id.ToString());
	}

	[Fact]
	public async Task Arquivar_RegistraMuralArquivar()
	{
		var trilha = new TrilhaFalsa();
		var encontrada = Existente();
		var handler = new ArquivarPublicacaoHandler(new PublicacaoRepositorioFalso(encontrada), trilha);

		await handler.HandleAsync(new ArquivarPublicacaoCommand(
			encontrada.Publicacao.Id,
			new HashSet<string>(["financeiro"], StringComparer.OrdinalIgnoreCase),
			ExigirVinculo: true));

		trilha.Registros.Should().ContainSingle()
			.Which.Verbo.Should().Be(VerbosDeAuditoria.MuralArquivar);
	}

	[Fact]
	public async Task Negado_NaoRegistra()
	{
		var trilha = new TrilhaFalsa();
		var handler = new ArquivarPublicacaoHandler(new PublicacaoRepositorioFalso(Existente()), trilha);

		var resultado = await handler.HandleAsync(new ArquivarPublicacaoCommand(
			Guid.NewGuid(),
			new HashSet<string>(["diretoria"], StringComparer.OrdinalIgnoreCase),
			ExigirVinculo: true));

		resultado.IsFailure.Should().BeTrue();
		trilha.Registros.Should().BeEmpty("o que nao aconteceu nao entra na trilha");
	}
}
