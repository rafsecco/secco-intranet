using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Teste unitário do handler de criação de setor (ADR-0012): sem infraestrutura, fake da porta.</summary>
public class CreateSetorHandlerTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class FakeRepository : ISetorRepository
	{
		public List<Setor> Added { get; } = [];

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default)
		{
			Added.Add(setor);
			return Task.CompletedTask;
		}

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(Added.FirstOrDefault(setor => setor.Id == id));

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
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
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), new ItemMenuRepositorioFalso());

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
		var handler = new CreateSetorHandler(new FakeRepository(), Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), new ItemMenuRepositorioFalso());

		var result = await handler.HandleAsync(new CreateSetorCommand(nome, "financeiro"));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(IntranetErrors.Setores.NomeRequired);
	}

	[Fact]
	public async Task Handle_WithNomeAboveLimit_ReturnsValidationFailure()
	{
		var handler = new CreateSetorHandler(new FakeRepository(), Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), new ItemMenuRepositorioFalso());

		var result = await handler.HandleAsync(
			new CreateSetorCommand(new string('x', Options.MaxNameLength + 1), "financeiro"));

		result.IsFailure.Should().BeTrue();
		result.Error.Type.Should().Be(ErrorType.Validation);
	}

	[Fact]
	public async Task Handle_WithDuplicateSlug_ReturnsConflictFailure()
	{
		var repository = new FakeRepository();
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), new ItemMenuRepositorioFalso());

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
		var handler = new CreateSetorHandler(repository, Options, provisioner, new TrilhaFalsa(), new ItemMenuRepositorioFalso());

		var result = await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(IntranetErrors.Setores.AccessProvisioningUnavailable);
		repository.Added.Should().BeEmpty();
	}

	[Fact]
	public async Task Handle_WithValidCommand_CallsProvisionerWithCommandSlug()
	{
		var provisioner = new FakeSetorAccessProvisioner();
		var handler = new CreateSetorHandler(new FakeRepository(), Options, provisioner, new TrilhaFalsa(), new ItemMenuRepositorioFalso());

		await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));

		provisioner.LastSlug.Should().Be("financeiro");
	}

	[Fact]
	public async Task Handle_ComOsDoisRecursosHabilitados_CriaRaizEOsDoisEmOrdemAlfabetica()
	{
		var repository = new FakeRepository();
		var itens = new ItemMenuRepositorioFalso();
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), itens);

		var resultado = await handler.HandleAsync(new CreateSetorCommand("Financeiro", "financeiro"));

		resultado.IsSuccess.Should().BeTrue();
		var setorId = resultado.Value.Id;
		var raiz = itens.Itens.Single(i => i.SetorId == setorId && i.Tipo == Secco.Intranet.Domain.Menu.TipoDeItemMenu.Setor);
		var avisos = itens.Itens.Single(i => i.SetorId == setorId && i.Tipo == Secco.Intranet.Domain.Menu.TipoDeItemMenu.Avisos);
		var documentos = itens.Itens.Single(i => i.SetorId == setorId && i.Tipo == Secco.Intranet.Domain.Menu.TipoDeItemMenu.Documentos);

		avisos.ParentId.Should().Be(raiz.Id);
		documentos.ParentId.Should().Be(raiz.Id);
		avisos.Ordem.Should().BeLessThan(documentos.Ordem, "Avisos vem antes de Documentos em ordem alfabética");
	}

	[Fact]
	public async Task Handle_ComOsDoisRecursosDesabilitados_SoCriaARaiz()
	{
		var repository = new FakeRepository();
		var itens = new ItemMenuRepositorioFalso();
		var handler = new CreateSetorHandler(repository, Options, new FakeSetorAccessProvisioner(), new TrilhaFalsa(), itens);

		var resultado = await handler.HandleAsync(
			new CreateSetorCommand("TI", "ti", HabilitarDocumentos: false, HabilitarAvisos: false));

		resultado.IsSuccess.Should().BeTrue();
		var setorId = resultado.Value.Id;
		itens.Itens.Where(i => i.SetorId == setorId).Should().ContainSingle(i => i.Tipo == Secco.Intranet.Domain.Menu.TipoDeItemMenu.Setor);
	}

	[Theory]
	[InlineData("mural")]
	[InlineData("Setores")]
	[InlineData(" acesso ")]
	[InlineData("setor")]
	[InlineData("api")]
	[InlineData(".well-known")]
	[InlineData("signin-oidc")]
	[InlineData("signout-callback-oidc")]
	public async Task Handle_ComSlugReservado_RecusaSemProvisionarRoles(string slug)
	{
		var repository = new FakeRepository();
		var provisioner = new FakeSetorAccessProvisioner();
		var handler = new CreateSetorHandler(repository, Options, provisioner, new TrilhaFalsa(), new ItemMenuRepositorioFalso());

		var resultado = await handler.HandleAsync(new CreateSetorCommand("Qualquer", slug));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Code.Should().Be("Intranet.Setor.SlugReservado");
		provisioner.LastSlug.Should().BeNull("a recusa vem antes de criar Role no SecureGate");
		repository.Added.Should().BeEmpty();
	}
}
