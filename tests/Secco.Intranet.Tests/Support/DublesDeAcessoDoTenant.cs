using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// Acesso de um tenant administrado em memória. Toda chamada fica em <see cref="Chamadas"/> com o
/// tenant recebido — é assim que os testes provam que o tenant da rota chegou à porta.
/// </summary>
public sealed class GestaoDeAcessoDoTenantFalsa : IGestaoDeAcessoDoTenant
{
	private readonly Dictionary<string, (bool Reservado, List<string> Permissoes)> _perfis = new(StringComparer.Ordinal);
	private readonly Dictionary<Guid, UsuarioDoTenantDetalheDto> _usuarios = [];

	/// <summary>Chamadas, na ordem.</summary>
	public List<string> Chamadas { get; } = [];

	/// <summary>Pessoas cadastradas por <see cref="CadastrarUsuarioAsync"/>.</summary>
	public List<NovoUsuarioDoTenant> Cadastrados { get; } = [];

	/// <summary>Federação devolvida pela leitura.</summary>
	public FederacaoDto Federacao { get; set; } = new(null, false, null);

	/// <summary>
	/// Catálogo de permissões da instalação. Como na plataforma, a permissão de um perfil que não
	/// está em nenhum produto daqui aparece como fora do catálogo.
	/// </summary>
	public IReadOnlyList<ProdutoDoCatalogoDto> Catalogo { get; set; } = [];

	/// <summary>Quando definido, toda chamada falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Acrescenta um perfil.</summary>
	public GestaoDeAcessoDoTenantFalsa ComPerfil(string nome, bool reservado = false, params string[] permissoes)
	{
		_perfis[nome] = (reservado, [.. permissoes]);

		return this;
	}

	/// <summary>Acrescenta uma pessoa.</summary>
	public GestaoDeAcessoDoTenantFalsa ComUsuario(
		Guid id, string email, SituacaoDoUsuario situacao, bool temSenha = false, bool entraPorSenha = true, params string[] perfis)
	{
		_usuarios[id] = new UsuarioDoTenantDetalheDto(
			id, email, null, situacao, null, [.. perfis], [], false, temSenha, entraPorSenha, VinculoNoTenant.Dono);

		return this;
	}

	/// <summary>
	/// Contas de outros tenants da instalação, por e-mail (ADR-0040 da plataforma): o e-mail é único
	/// na instalação, então cadastrar uma delas falha, e incluí-la como membro funciona.
	/// </summary>
	public Dictionary<string, Guid> ContasDaInstalacao { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Acrescenta uma pessoa que é membro (dona de outro tenant), ativa ou suspensa.</summary>
	public GestaoDeAcessoDoTenantFalsa ComMembro(Guid id, string email, bool suspenso = false, params string[] perfis)
	{
		_usuarios[id] = new UsuarioDoTenantDetalheDto(
			id, email, null, SituacaoDoUsuario.Ativo, null, [.. perfis], [], false, true, true,
			suspenso ? VinculoNoTenant.Suspenso : VinculoNoTenant.Membro);

		return this;
	}

	/// <summary>Vínculo atual de uma pessoa.</summary>
	public VinculoNoTenant VinculoDe(Guid usuarioId) => _usuarios[usuarioId].Vinculo;

	/// <summary>Se a pessoa ainda está no tenant (remover o vínculo a tira).</summary>
	public bool EstaNoTenant(Guid usuarioId) => _usuarios.ContainsKey(usuarioId);

	/// <summary>Permissões gravadas de um perfil.</summary>
	public IReadOnlyList<string> PermissoesDe(string perfil) => _perfis[perfil].Permissoes;

	private Result Registrar(string chamada)
	{
		Chamadas.Add(chamada);

		return FalharCom is { } erro ? Result.Failure(erro) : Result.Success();
	}

	private Result<T> Registrar<T>(string chamada, Func<Result<T>> valor)
	{
		Chamadas.Add(chamada);

		return FalharCom is { } erro ? Result.Failure<T>(erro) : valor();
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar<IReadOnlyList<PerfilDoTenantDto>>($"listarPerfis:{tenantId}", () =>
			Result.Success<IReadOnlyList<PerfilDoTenantDto>>(
				[.. _perfis.Select(p => new PerfilDoTenantDto(p.Key, p.Value.Permissoes.Count, p.Value.Reservado))])));

	/// <inheritdoc />
	public Task<Result<PerfilDoTenantDetalheDto>> ObterPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"obterPerfil:{tenantId}:{nome}", () =>
			_perfis.TryGetValue(nome, out var perfil)
				? Result.Success(new PerfilDoTenantDetalheDto(
					nome, perfil.Permissoes, perfil.Reservado, Membros(nome).Count, ForaDoCatalogo(perfil.Permissoes)))
				: Result.Failure<PerfilDoTenantDetalheDto>(IntranetErrors.Acesso.PerfilNaoEncontrado)));

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"listarMembros:{tenantId}:{nome}:{pagina}", () =>
		{
			var membros = Membros(nome);

			return Result.Success(new PaginaDeMembros(
				[.. membros.Select(u => new MembroDoPerfilDto(u.Id, u.Email, u.Situacao))], pagina, 1, membros.Count));
		}));

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDoTenantDto>>> ListarUsuariosAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar<IReadOnlyList<UsuarioDoTenantDto>>($"listarUsuarios:{tenantId}", () =>
			Result.Success<IReadOnlyList<UsuarioDoTenantDto>>(
				[.. _usuarios.Values.Select(u => new UsuarioDoTenantDto(u.Id, u.Email, u.Situacao, u.Perfis, u.Nome, u.Vinculo))])));

	/// <inheritdoc />
	public Task<Result<UsuarioDoTenantDetalheDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"obterUsuario:{tenantId}:{usuarioId}", () =>
			_usuarios.TryGetValue(usuarioId, out var usuario)
				? Result.Success(usuario)
				: Result.Failure<UsuarioDoTenantDetalheDto>(IntranetErrors.Acesso.UsuarioNaoEncontrado)));

	/// <inheritdoc />
	public Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"obterFederacao:{tenantId}", () => Result.Success(Federacao)));

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"criarPerfil:{tenantId}:{nome}");

		if (registrado.IsSuccess)
		{
			_perfis.TryAdd(nome, (false, []));
		}

		return Task.FromResult(registrado);
	}

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(Guid tenantId, string nome, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"excluirPerfil:{tenantId}:{nome}");

		if (registrado.IsSuccess)
		{
			_perfis.Remove(nome);
		}

		return Task.FromResult(registrado);
	}

	/// <inheritdoc />
	public Task<Result> DefinirPermissoesDoPerfilAsync(
		Guid tenantId, string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"definirPermissoes:{tenantId}:{nome}");

		if (registrado.IsSuccess)
		{
			_perfis[nome] = (_perfis[nome].Reservado, [.. permissoes]);
		}

		return Task.FromResult(registrado);
	}

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(AlterarPerfis($"atribuir:{tenantId}:{usuarioId}:{perfil}", usuarioId, perfis => perfis.Append(perfil).Distinct()));

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Task.FromResult(AlterarPerfis($"retirar:{tenantId}:{usuarioId}:{perfil}", usuarioId, perfis => perfis.Where(p => p != perfil)));

	/// <inheritdoc />
	public Task<Result<Guid>> CadastrarUsuarioAsync(Guid tenantId, NovoUsuarioDoTenant novo, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"cadastrar:{tenantId}", () =>
		{
			if (ContasDaInstalacao.ContainsKey(novo.Email) || _usuarios.Values.Any(u => string.Equals(u.Email, novo.Email, StringComparison.OrdinalIgnoreCase)))
			{
				return Result.Failure<Guid>(IntranetErrors.Acesso.EmailJaCadastrado);
			}

			Cadastrados.Add(novo);
			var id = Guid.NewGuid();
			_usuarios[id] = new UsuarioDoTenantDetalheDto(
				id, novo.Email, novo.Nome, SituacaoDoUsuario.Ativo, null, novo.Perfis, [], false, false, novo.EntraPorSenha, VinculoNoTenant.Dono);

			return Result.Success(id);
		}));

	/// <inheritdoc />
	public Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"reenviarConvite:{tenantId}:{usuarioId}"));

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"desativar:{tenantId}:{usuarioId}"));

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"reativar:{tenantId}:{usuarioId}"));

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"encerrarSessoes:{tenantId}:{usuarioId}"));

	/// <inheritdoc />
	public Task<Result> DefinirFederacaoAsync(Guid tenantId, Guid directoryId, bool habilitada, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"definirFederacao:{tenantId}:{directoryId}:{habilitada}");

		if (registrado.IsSuccess)
		{
			Federacao = new FederacaoDto(directoryId, habilitada, DateTimeOffset.UtcNow);
		}

		return Task.FromResult(registrado);
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<ProdutoDoCatalogoDto>>> ListarCatalogoDePermissoesAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar("listarCatalogo", () => Result.Success(Catalogo)));

	/// <inheritdoc />
	public Task<Result<Guid>> IncluirMembroAsync(Guid tenantId, string email, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"incluirMembro:{tenantId}:{email}", () =>
		{
			if (_usuarios.Values.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
			{
				return Result.Failure<Guid>(IntranetErrors.Acesso.JaEMembro);
			}

			if (!ContasDaInstalacao.TryGetValue(email, out var id))
			{
				return Result.Failure<Guid>(IntranetErrors.Acesso.UsuarioNaoEncontrado);
			}

			ComMembro(id, email);

			return Result.Success(id);
		}));

	/// <inheritdoc />
	public Task<Result> SuspenderVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(MudarVinculo($"suspenderVinculo:{tenantId}:{usuarioId}", usuarioId, VinculoNoTenant.Suspenso));

	/// <inheritdoc />
	public Task<Result> ReativarVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Task.FromResult(MudarVinculo($"reativarVinculo:{tenantId}:{usuarioId}", usuarioId, VinculoNoTenant.Membro));

	/// <inheritdoc />
	public Task<Result> RemoverVinculoAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var registrado = Registrar($"removerVinculo:{tenantId}:{usuarioId}");

		if (registrado.IsSuccess)
		{
			_usuarios.Remove(usuarioId);
		}

		return Task.FromResult(registrado);
	}

	private Result MudarVinculo(string chamada, Guid usuarioId, VinculoNoTenant vinculo)
	{
		var registrado = Registrar(chamada);

		if (registrado.IsSuccess && _usuarios.TryGetValue(usuarioId, out var usuario))
		{
			_usuarios[usuarioId] = usuario with { Vinculo = vinculo };
		}

		return registrado;
	}

	private List<string> ForaDoCatalogo(IEnumerable<string> permissoes)
	{
		var conhecidas = Catalogo.SelectMany(produto => produto.Permissoes).Select(p => p.Nome).ToHashSet(StringComparer.Ordinal);

		return [.. permissoes.Where(p => !conhecidas.Contains(p))];
	}

	private List<UsuarioDoTenantDetalheDto> Membros(string perfil) =>
		[.. _usuarios.Values.Where(u => u.Perfis.Contains(perfil, StringComparer.Ordinal))];

	private Result AlterarPerfis(string chamada, Guid usuarioId, Func<IEnumerable<string>, IEnumerable<string>> alterar)
	{
		var registrado = Registrar(chamada);

		if (registrado.IsFailure)
		{
			return registrado;
		}

		if (!_usuarios.TryGetValue(usuarioId, out var usuario))
		{
			return Result.Failure(IntranetErrors.Acesso.UsuarioNaoEncontrado);
		}

		_usuarios[usuarioId] = usuario with { Perfis = [.. alterar(usuario.Perfis)] };

		return Result.Success();
	}
}
