using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Autorização de escrita. Quem não administra o setor recebe o MESMO erro de publicação
/// inexistente — distinguir revelaria a existência.
/// </summary>
public class PublicacaoAutorizacaoTests
{
	private sealed class RepositorioFalso(PublicacaoComSetor? resultado) : IPublicacaoRepository
	{
		public bool Gravou { get; private set; }

		public Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			Gravou = true;

			return Task.CompletedTask;
		}

		public Task<PublicacaoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(resultado);

		public Task<PagedResult<PublicacaoDto>> ListarNoMuralAsync(
			MuralCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Create<PublicacaoDto>([], criteria.Page, 0));

		public Task<IReadOnlyList<PublicacaoDto>> ListarDoSetorAsync(
			string setorSlug, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<PublicacaoDto>>([]);
	}

	private static PublicacaoComSetor Existente() =>
		new(new Publicacao(Guid.NewGuid(), "Comunicado", "Corpo", TipoPublicacao.Aviso,
				Visibilidade.Empresa, PrioridadePublicacao.Normal,
				DateTimeOffset.UtcNow, null, "quem.publicou"),
			"Financeiro", "financeiro");

	private static HashSet<string> Setores(params string[] slugs) =>
		new(slugs, StringComparer.OrdinalIgnoreCase);

	[Fact]
	public async Task Arquivar_SemAdministrarOSetor_Nega()
	{
		var repositorio = new RepositorioFalso(Existente());
		var handler = new ArquivarPublicacaoHandler(repositorio);

		var resultado = await handler.HandleAsync(new ArquivarPublicacaoCommand(
			Guid.NewGuid(), Setores("diretoria"), ExigirVinculo: true));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Publicacoes.NotFound);
		repositorio.Gravou.Should().BeFalse("negar precisa impedir a escrita, não só a resposta");
	}

	[Fact]
	public async Task Arquivar_AdministrandoOSetor_Libera()
	{
		var repositorio = new RepositorioFalso(Existente());
		var handler = new ArquivarPublicacaoHandler(repositorio);

		var resultado = await handler.HandleAsync(new ArquivarPublicacaoCommand(
			Guid.NewGuid(), Setores("financeiro"), ExigirVinculo: true));

		resultado.IsSuccess.Should().BeTrue();
		repositorio.Gravou.Should().BeTrue();
	}

	[Fact]
	public async Task Arquivar_Inexistente_DevolveOMesmoErroDeNegado()
	{
		var negado = await new ArquivarPublicacaoHandler(new RepositorioFalso(Existente()))
			.HandleAsync(new ArquivarPublicacaoCommand(Guid.NewGuid(), Setores(), ExigirVinculo: true));

		var inexistente = await new ArquivarPublicacaoHandler(new RepositorioFalso(null))
			.HandleAsync(new ArquivarPublicacaoCommand(Guid.NewGuid(), Setores(), ExigirVinculo: true));

		negado.Error.Code.Should().Be(inexistente.Error.Code);
	}

	[Fact]
	public async Task Editar_ComExpiracaoInvalida_DevolveResultadoDeFalha()
	{
		var agora = DateTimeOffset.UtcNow;
		var handler = new EditarPublicacaoHandler(new RepositorioFalso(Existente()), new IntranetOptions());

		var resultado = await handler.HandleAsync(new EditarPublicacaoCommand(
			Guid.NewGuid(), Setores("financeiro"), ExigirVinculo: true,
			"Titulo", "Corpo", TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, agora, agora.AddMinutes(-5)));

		resultado.IsFailure.Should().BeTrue(
			"invariante de domínio violada por entrada de usuário vira Result, não exceção (ADR-0004)");
		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ExpiracaoInvalida);
	}

	[Fact]
	public async Task Editar_ComTituloAcimaDoLimiteConfigurado_Recusa()
	{
		var handler = new EditarPublicacaoHandler(
			new RepositorioFalso(Existente()), new IntranetOptions { MaxNameLength = 10 });

		var resultado = await handler.HandleAsync(new EditarPublicacaoCommand(
			Guid.NewGuid(), Setores("financeiro"), ExigirVinculo: true,
			new string('t', 11), "Corpo", TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, DateTimeOffset.UtcNow, null));

		resultado.IsFailure.Should().BeTrue(
			"o limite configurado vale na edição também: aplicá-lo só na criação abriria a porta pela edição");
		resultado.Error.Code.Should().Be(IntranetErrors.Publicacoes.TituloTooLong(10).Code);
	}
}
