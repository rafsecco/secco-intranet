using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Edição de setor. O slug fica de fora de propósito: ele é a base das Roles
/// <c>{slug}-admin</c>/<c>{slug}-user</c> no SecureGate (ADR-0001), e trocá-lo romperia o
/// vínculo de todos os usuários do setor sem que nada aqui percebesse.
/// </summary>
public class EditarSetorHandlerTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class RepositorioFalso(Setor? setor) : ISetorRepository
	{
		public bool Gravou { get; private set; }

		public Task AddAsync(Setor novo, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(setor);

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(setor);

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(false);

		public Task<PagedResult<Setor>> SearchAsync(
			SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<Setor>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			Gravou = true;

			return Task.CompletedTask;
		}
	}

	private static EditarSetorCommand Comando(
		string? nome = "Financeiro e Controladoria",
		string? icone = "bi-cash-coin",
		bool ativo = true) =>
		new(Guid.NewGuid(), nome, icone, ativo);

	[Fact]
	public async Task Edita_NomeEIcone()
	{
		var repositorio = new RepositorioFalso(new Setor("Financeiro", "financeiro"));
		var handler = new EditarSetorHandler(repositorio, new IntranetOptions(), new TrilhaFalsa());

		var resultado = await handler.HandleAsync(Comando());

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Nome.Should().Be("Financeiro e Controladoria");
		resultado.Value.Icone.Should().Be("bi-cash-coin");
		repositorio.Gravou.Should().BeTrue();
	}

	[Fact]
	public async Task NaoMudaOSlug()
	{
		var repositorio = new RepositorioFalso(new Setor("Financeiro", "financeiro"));
		var handler = new EditarSetorHandler(repositorio, new IntranetOptions(), new TrilhaFalsa());

		var resultado = await handler.HandleAsync(Comando(nome: "Outro Nome"));

		resultado.Value.Slug.Should().Be(
			"financeiro", "o slug é a base das Roles do SecureGate e não é editável");
	}

	[Fact]
	public async Task Inexistente_Recusa()
	{
		var handler = new EditarSetorHandler(new RepositorioFalso(null), new IntranetOptions(), new TrilhaFalsa());

		var resultado = await handler.HandleAsync(Comando());

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Setores.NotFound);
	}

	[Fact]
	public async Task IconeForaDoPadrao_Recusa()
	{
		var repositorio = new RepositorioFalso(new Setor("Financeiro", "financeiro"));
		var handler = new EditarSetorHandler(repositorio, new IntranetOptions(), new TrilhaFalsa());

		var resultado = await handler.HandleAsync(Comando(icone: "d-none"));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Setores.IconeInvalido);
		repositorio.Gravou.Should().BeFalse("recusar precisa impedir a escrita, não só a resposta");
	}

	[Fact]
	public async Task SemNome_Recusa()
	{
		var repositorio = new RepositorioFalso(new Setor("Financeiro", "financeiro"));
		var handler = new EditarSetorHandler(repositorio, new IntranetOptions(), new TrilhaFalsa());

		var resultado = await handler.HandleAsync(Comando(nome: "   "));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Setores.NomeRequired);
	}

	[Fact]
	public async Task Desativa_QuandoNaoEFixo()
	{
		var repositorio = new RepositorioFalso(new Setor("Financeiro", "financeiro"));
		var handler = new EditarSetorHandler(repositorio, new IntranetOptions(), new TrilhaFalsa());

		var resultado = await handler.HandleAsync(Comando(ativo: false));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Ativo.Should().BeFalse();
	}

	[Fact]
	public async Task SetorFixo_NaoPodeSerDesativado()
	{
		var repositorio = new RepositorioFalso(new Setor("Infraestrutura", "infraestrutura", fixo: true));
		var handler = new EditarSetorHandler(repositorio, new IntranetOptions(), new TrilhaFalsa());

		var resultado = await handler.HandleAsync(Comando(ativo: false));

		resultado.IsFailure.Should().BeTrue(
			"o domínio recusa com exceção; entrada de usuário precisa virar Result (ADR-0004)");
		resultado.Error.Should().Be(IntranetErrors.Setores.FixoNaoDesativa);
		repositorio.Gravou.Should().BeFalse();
	}

	[Fact]
	public async Task SetorFixo_ContinuaEditavelNoResto()
	{
		var repositorio = new RepositorioFalso(new Setor("Infraestrutura", "infraestrutura", fixo: true));
		var handler = new EditarSetorHandler(repositorio, new IntranetOptions(), new TrilhaFalsa());

		var resultado = await handler.HandleAsync(Comando(nome: "Infra", icone: "bi-hdd-rack", ativo: true));

		resultado.IsSuccess.Should().BeTrue("ser fixo impede desativar, não impede renomear");
		resultado.Value.Icone.Should().Be("bi-hdd-rack");
	}
}
