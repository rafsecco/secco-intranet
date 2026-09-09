using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Os verbos de Setor. Desativar ganha verbo próprio porque muda quem enxerga o quê — e um
/// "setor.editar" genérico esconderia exatamente a mudança que alguém vai procurar.
/// </summary>
public class AuditoriaDeSetorTests
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

	private sealed class RepositorioFalso(Setor? setor) : ISetorRepository
	{
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

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class ProvisionerFalso : ISetorAccessProvisioner
	{
		public Task<Result> EnsureSetorRolesAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(Result.Success());
	}

	private static EditarSetorHandler Montar(Setor setor, TrilhaFalsa trilha) =>
		new(new RepositorioFalso(setor), new IntranetOptions(), trilha);

	[Fact]
	public async Task Criar_RegistraSetorCriar()
	{
		var trilha = new TrilhaFalsa();
		var handler = new CreateSetorHandler(
			new RepositorioFalso(setor: null), new IntranetOptions(), new ProvisionerFalso(), trilha);

		var resultado = await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));

		resultado.IsSuccess.Should().BeTrue();

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.SetorCriar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Setor);
		registro.RecursoId.Should().Be(resultado.Value.Id.ToString());
		JsonDocument.Parse(registro.Metadata!).RootElement.GetProperty("slug").GetString()
			.Should().Be("financeiro");
	}

	[Fact]
	public async Task Editar_SoNomeEIcone_RegistraSetorEditar()
	{
		var trilha = new TrilhaFalsa();
		var setor = new Setor("Financeiro", "financeiro");

		await Montar(setor, trilha).HandleAsync(
			new EditarSetorCommand(Guid.NewGuid(), "Financeiro e Controladoria", "bi-cash-coin", Ativo: true));

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.SetorEditar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Setor);
	}

	[Fact]
	public async Task Editar_Desativando_RegistraSetorDesativar()
	{
		var trilha = new TrilhaFalsa();
		var setor = new Setor("Financeiro", "financeiro");

		await Montar(setor, trilha).HandleAsync(
			new EditarSetorCommand(Guid.NewGuid(), "Financeiro", "bi-cash-coin", Ativo: false));

		trilha.Registros.Should().ContainSingle()
			.Which.Verbo.Should().Be(VerbosDeAuditoria.SetorDesativar,
				"desativar muda quem enxerga o quê, e um 'editar' genérico esconderia isso");
	}

	[Fact]
	public async Task Editar_Reativando_RegistraSetorReativar()
	{
		var trilha = new TrilhaFalsa();
		var setor = new Setor("Financeiro", "financeiro");
		setor.Desativar();

		await Montar(setor, trilha).HandleAsync(
			new EditarSetorCommand(Guid.NewGuid(), "Financeiro", "bi-cash-coin", Ativo: true));

		trilha.Registros.Should().ContainSingle()
			.Which.Verbo.Should().Be(VerbosDeAuditoria.SetorReativar);
	}

	[Fact]
	public async Task Editar_Recusado_NaoRegistra()
	{
		var trilha = new TrilhaFalsa();
		var fixo = new Setor("Infraestrutura", "infraestrutura", fixo: true);

		var resultado = await Montar(fixo, trilha).HandleAsync(
			new EditarSetorCommand(Guid.NewGuid(), "Infra", "bi-hdd-rack", Ativo: false));

		resultado.IsFailure.Should().BeTrue();
		trilha.Registros.Should().BeEmpty();
	}
}
