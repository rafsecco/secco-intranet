using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Domain.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>Usuários ativos em memória.</summary>
public sealed class UsuariosParaDiretorioFalso : IUsuariosParaDiretorio
{
	/// <summary>Usuários ativos devolvidos.</summary>
	public List<UsuarioParaDiretorio> Usuarios { get; } = [];

	/// <summary>Quando não nulo, a listagem falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Quantas vezes a lista foi pedida.</summary>
	public int Chamadas { get; private set; }

	/// <summary>Acrescenta um usuário ativo.</summary>
	public UsuariosParaDiretorioFalso Com(Guid id, string email)
	{
		Usuarios.Add(new UsuarioParaDiretorio(id, email));

		return this;
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(CancellationToken cancellationToken = default)
	{
		Chamadas++;

		IReadOnlyList<UsuarioParaDiretorio> lista = [.. Usuarios];

		return Task.FromResult(FalharCom is null
			? Result.Success(lista)
			: Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(FalharCom));
	}
}

/// <summary>Repositório de perfis em memória.</summary>
public sealed class PerfisColaboradorFalso : IPerfilColaboradorRepository
{
	/// <summary>Perfis existentes.</summary>
	public List<PerfilColaborador> Perfis { get; } = [];

	/// <summary>Quantas vezes <see cref="SaveChangesAsync"/> foi chamado.</summary>
	public int Salvou { get; private set; }

	/// <summary>Acrescenta um perfil já pronto.</summary>
	public PerfisColaboradorFalso Com(PerfilColaborador perfil)
	{
		Perfis.Add(perfil);

		return this;
	}

	/// <inheritdoc />
	public Task<PerfilColaborador?> GetByUsuarioIdAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Perfis.FirstOrDefault(perfil => perfil.UsuarioId == usuarioId));

	/// <inheritdoc />
	public Task<PerfilColaborador?> GetParaEdicaoAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		GetByUsuarioIdAsync(usuarioId, cancellationToken);

	/// <inheritdoc />
	public Task<IReadOnlyList<PerfilColaborador>> ListarTodosAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<PerfilColaborador>>([.. Perfis]);

	/// <inheritdoc />
	public Task<bool> TentarAdicionarAsync(PerfilColaborador perfil, CancellationToken cancellationToken = default)
	{
		if (Perfis.Any(existente => existente.UsuarioId == perfil.UsuarioId))
		{
			return Task.FromResult(false);
		}

		Perfis.Add(perfil);
		Salvou++;

		return Task.FromResult(true);
	}

	/// <inheritdoc />
	public Task SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		Salvou++;

		return Task.CompletedTask;
	}
}

/// <summary>Setores em memória; só a leitura que o diretório usa.</summary>
public sealed class SetoresFalsos : ISetorRepository
{
	/// <summary>Setores existentes.</summary>
	public List<Setor> Setores { get; } = [];

	/// <summary>Acrescenta um setor; <paramref name="ativo"/> falso o desativa.</summary>
	public Setor Com(string nome, string slug, bool ativo = true)
	{
		var setor = new Setor(nome, slug);

		if (!ativo)
		{
			setor.Desativar();
		}

		Setores.Add(setor);

		return setor;
	}

	/// <inheritdoc />
	public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(Setores.FirstOrDefault(setor => setor.Id == id));

	/// <inheritdoc />
	public Task<PagedResult<Setor>> SearchAsync(SetorSearchCriteria criteria, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<Setor> itens = [.. Setores.Where(setor => !criteria.ApenasAtivos || setor.Ativo)];

		return Task.FromResult(PagedResult.Create(itens, new PageRequest(1, 200), itens.Count));
	}

	/// <inheritdoc />
	public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	/// <inheritdoc />
	public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
		Task.FromResult(Setores.FirstOrDefault(setor => string.Equals(setor.Slug, slug, StringComparison.OrdinalIgnoreCase)));

	/// <inheritdoc />
	public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	/// <inheritdoc />
	public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	/// <inheritdoc />
	public Task SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
