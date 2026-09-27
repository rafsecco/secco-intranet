using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ReconciliarPermissoesHandlerTests
{
	/// <summary>
	/// Repositório fake mínimo, no molde do <c>FakeRepository</c> privado de outros testes de
	/// setor — implementa só o que <see cref="SearchSetoresHandler"/> chama; o resto lança.
	/// Devolve tudo numa página só (os testes não passam de poucos setores).
	/// </summary>
	private sealed class FakeSetorRepository : ISetorRepository
	{
		private readonly List<Setor> _setores = [];

		public FakeSetorRepository Com(string slug)
		{
			_setores.Add(new Setor(slug, slug));

			return this;
		}

		public Task<PagedResult<Setor>> SearchAsync(SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(new PagedResult<Setor>(_setores, PageRequest.FirstPage, Math.Max(_setores.Count, 1), _setores.Count));

		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => throw new NotImplementedException();

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
	}

	[Fact]
	public async Task GarantePermissaoDeCadaSetorEDosPerfisDoDiretorioSeExistirem()
	{
		var repositorio = new FakeSetorRepository().Com("financeiro").Com("ti");
		var gestao = new GestaoDeAcessoFalsa()
			.ComPerfil("financeiro-admin").ComPerfil("financeiro-user")
			.ComPerfil("ti-admin").ComPerfil("ti-user")
			.ComPerfil("diretorio-admin").ComPerfil("diretorio-user");
		var trilha = new TrilhaDeAcessoFalsa();
		var handler = new ReconciliarPermissoesHandler(new SearchSetoresHandler(repositorio), gestao, trilha);

		var resultado = await handler.HandleAsync();

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Setores.Should().Be(2);
		resultado.Value.PerfisDoDiretorio.Should().Be(2);
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoPermissoesReconciliar);
		gestao.Perfis.Single(p => p.Nome == "financeiro-admin").Permissoes.Should().BeEquivalentTo(
			IntranetPermissoes.Setor.Read("financeiro"), IntranetPermissoes.Setor.Write("financeiro"));
		gestao.Perfis.Single(p => p.Nome == "financeiro-user").Permissoes.Should().BeEquivalentTo(IntranetPermissoes.Setor.Read("financeiro"));
		gestao.Perfis.Single(p => p.Nome == "diretorio-user").Permissoes.Should().BeEquivalentTo(IntranetPermissoes.Diretorio.Read);
		gestao.Perfis.Single(p => p.Nome == "diretorio-admin").Permissoes.Should().BeEquivalentTo(
			IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Diretorio.Manage);
	}

	[Fact]
	public async Task SemDiretorioAdminOuUsuario_NaoContaNenhumPerfilDoDiretorio()
	{
		var repositorio = new FakeSetorRepository();
		var gestao = new GestaoDeAcessoFalsa();
		var handler = new ReconciliarPermissoesHandler(new SearchSetoresHandler(repositorio), gestao, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync();

		resultado.Value.PerfisDoDiretorio.Should().Be(0);
	}

	[Fact]
	public async Task Idempotente_RodarDeNovoNaoFalhaNemDuplica()
	{
		var repositorio = new FakeSetorRepository().Com("financeiro");
		var gestao = new GestaoDeAcessoFalsa().ComPerfil("financeiro-admin").ComPerfil("financeiro-user");
		var handler = new ReconciliarPermissoesHandler(new SearchSetoresHandler(repositorio), gestao, new TrilhaDeAcessoFalsa());

		await handler.HandleAsync();
		var segunda = await handler.HandleAsync();

		segunda.IsSuccess.Should().BeTrue();
		gestao.Perfis.Single(p => p.Nome == "financeiro-admin").Permissoes.Should().BeEquivalentTo(
			IntranetPermissoes.Setor.Read("financeiro"), IntranetPermissoes.Setor.Write("financeiro"));
	}

	[Fact]
	public async Task PreservaPermissaoExtraJaGravada()
	{
		var repositorio = new FakeSetorRepository().Com("financeiro");
		var gestao = new GestaoDeAcessoFalsa().ComPerfil("financeiro-admin", reservado: false, "extra:read").ComPerfil("financeiro-user");
		var handler = new ReconciliarPermissoesHandler(new SearchSetoresHandler(repositorio), gestao, new TrilhaDeAcessoFalsa());

		await handler.HandleAsync();

		gestao.Perfis.Single(p => p.Nome == "financeiro-admin").Permissoes.Should().BeEquivalentTo(
			"extra:read", IntranetPermissoes.Setor.Read("financeiro"), IntranetPermissoes.Setor.Write("financeiro"));
	}
}
