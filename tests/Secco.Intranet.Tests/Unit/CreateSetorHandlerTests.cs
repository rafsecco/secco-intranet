using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Teste unitário do handler de criação de setor (ADR-0012): sem infraestrutura, fake da porta.</summary>
public class CreateSetorHandlerTests
{
	private sealed class FakeRepository : ISetorRepository
	{
		public List<Setor> Added { get; } = [];

		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default)
		{
			Added.Add(setor);
			return Task.CompletedTask;
		}

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(Added.FirstOrDefault(setor => setor.Id == id));

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(Added.FirstOrDefault(setor => setor.Slug == slug));

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(Added.Any(setor => setor.Slug == slug));

		public Task<PagedResult<Setor>> SearchAsync(SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Create(Added, criteria.EffectivePage, Added.Count));
	}

	/// <summary>Fake da porta de provisionamento (ADR-0001): configurável para simular sucesso/falha e capturar o slug recebido.</summary>
	private sealed class FakeSetorAccessProvisioner : ISetorAccessProvisioner
	{
		public Result ResultToReturn { get; set; } = Result.Success();

		public string? LastSlug { get; private set; }

		public Task<Result> EnsureSetorRolesAsync(string slug, CancellationToken cancellationToken = default)
		{
			LastSlug = slug;
			return Task.FromResult(ResultToReturn);
		}
	}

	private static readonly IntranetOptions Options = new();

	[Fact]
	public async Task Handle_WithValidCommand_PersistsAndReturnsDto()
	{
		var repository = new FakeRepository();
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner());

		var result = await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));

		result.IsSuccess.Should().BeTrue();
		repository.Added.Should().ContainSingle().Which.Id.Should().Be(result.Value.Id);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Handle_WithoutNome_ReturnsValidationFailure(string? nome)
	{
		var handler = new CreateSetorHandler(new FakeRepository(), Options, new FakeSetorAccessProvisioner());

		var result = await handler.HandleAsync(new CreateSetorCommand(nome, "financeiro"));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(IntranetErrors.Setores.NomeRequired);
	}

	[Fact]
	public async Task Handle_WithNomeAboveLimit_ReturnsValidationFailure()
	{
		var handler = new CreateSetorHandler(new FakeRepository(), Options, new FakeSetorAccessProvisioner());

		var result = await handler.HandleAsync(
			new CreateSetorCommand(new string('x', Options.MaxNameLength + 1), "financeiro"));

		result.IsFailure.Should().BeTrue();
		result.Error.Type.Should().Be(ErrorType.Validation);
	}

	[Fact]
	public async Task Handle_WithDuplicateSlug_ReturnsConflictFailure()
	{
		var repository = new FakeRepository();
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner());

		await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));
		var result = await handler.HandleAsync(new CreateSetorCommand("Financeiro Filial", "financeiro"));

		result.IsFailure.Should().BeTrue();
		result.Error.Type.Should().Be(ErrorType.Conflict);
	}

	[Fact]
	public async Task Handle_WhenProvisionerFails_ReturnsFailureAndDoesNotPersist()
	{
		var repository = new FakeRepository();
		var provisioner = new FakeSetorAccessProvisioner
		{
			ResultToReturn = Result.Failure(IntranetErrors.Setores.AccessProvisioningUnavailable),
		};
		var handler = new CreateSetorHandler(repository, Options, provisioner);

		var result = await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(IntranetErrors.Setores.AccessProvisioningUnavailable);
		repository.Added.Should().BeEmpty();
	}

	[Fact]
	public async Task Handle_WithValidCommand_CallsProvisionerWithCommandSlug()
	{
		var provisioner = new FakeSetorAccessProvisioner();
		var handler = new CreateSetorHandler(new FakeRepository(), Options, provisioner);

		await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));

		provisioner.LastSlug.Should().Be("financeiro");
	}
}
