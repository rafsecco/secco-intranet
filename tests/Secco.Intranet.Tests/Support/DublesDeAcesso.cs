using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>Trilha que guarda o que foi registrado.</summary>
public sealed class TrilhaDeAcessoFalsa : ITrilhaDeAuditoria
{
	/// <summary>Registros na ordem em que chegaram.</summary>
	public List<RegistroDeAuditoria> Registros { get; } = [];

	/// <inheritdoc />
	public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
	{
		Registros.Add(registro);

		return Task.CompletedTask;
	}
}

/// <summary>Ator fixo (ou ausente) para os handlers que comparam "quem agiu" com o alvo.</summary>
/// <param name="usuarioId">Id do ator; <c>null</c> simula o modo aberto de DEV.</param>
public sealed class AtorDeAcessoFalso(Guid? usuarioId) : IAtorAtual
{
	/// <inheritdoc />
	public AtorDaAcao? Atual() =>
		usuarioId is null ? null : new AtorDaAcao(usuarioId.Value.ToString(), "admin@exemplo.com");
}

/// <summary>
/// Gestão de acesso em memória. As chamadas de escrita ficam em <see cref="Chamadas"/>; os
/// membros de um perfil saem dos <see cref="Usuarios"/> que o têm, então atribuir e retirar
/// mudam o que as leituras seguintes devolvem.
/// </summary>
public sealed class GestaoDeAcessoFalsa : IGestaoDeAcesso
{
	/// <summary>Perfis existentes.</summary>
	public List<PerfilDetalheDto> Perfis { get; } = [];

	/// <summary>Usuários existentes.</summary>
	public List<UsuarioDetalheDto> Usuarios { get; } = [];

	/// <summary>Escritas recebidas, no formato <c>acao:alvo</c>.</summary>
	public List<string> Chamadas { get; } = [];

	/// <summary>Quando não nulo, toda escrita falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Tamanho da página de membros — reduza para exercitar a paginação.</summary>
	public int TamanhoDaPagina { get; set; } = 100;

	/// <summary>Acrescenta um perfil.</summary>
	public GestaoDeAcessoFalsa ComPerfil(string nome, bool reservado = false, params string[] permissoes)
	{
		Perfis.Add(new PerfilDetalheDto(nome, permissoes, reservado, 0, ClassificacaoDePerfil.Tipo(nome)));

		return this;
	}

	/// <summary>Acrescenta um usuário.</summary>
	public GestaoDeAcessoFalsa ComUsuario(
		Guid id, string email, SituacaoDoUsuario situacao = SituacaoDoUsuario.Ativo, params string[] perfis)
	{
		Usuarios.Add(new UsuarioDetalheDto(id, email, situacao, null, perfis, [], [], false));

		return this;
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<PerfilDto> lista =
			[.. Perfis.Select(p => new PerfilDto(p.Nome, p.Permissoes.Count, p.Tipo, p.Reservado))];

		return Task.FromResult(Result.Success(lista));
	}

	/// <inheritdoc />
	public Task<Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default)
	{
		var perfil = Perfis.FirstOrDefault(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase));

		return Task.FromResult(perfil is null
			? Result.Failure<PerfilDetalheDto>(IntranetErrors.Acesso.PerfilNaoEncontrado)
			: Result.Success(perfil with { TotalDeMembros = MembrosDe(nome).Count }));
	}

	/// <inheritdoc />
	public Task<Result<PaginaDeMembros>> ListarMembrosAsync(
		string nome, int pagina, CancellationToken cancellationToken = default)
	{
		var todos = MembrosDe(nome);
		var totalDePaginas = todos.Count == 0 ? 0 : (int)Math.Ceiling(todos.Count / (double)TamanhoDaPagina);
		IReadOnlyList<MembroDoPerfilDto> itens = [.. todos.Skip((pagina - 1) * TamanhoDaPagina).Take(TamanhoDaPagina)];

		return Task.FromResult(Result.Success(new PaginaDeMembros(itens, pagina, totalDePaginas, todos.Count)));
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<UsuarioDto> lista = [.. Usuarios.Select(u => new UsuarioDto(u.Id, u.Email, u.Situacao, u.Perfis))];

		return Task.FromResult(Result.Success(lista));
	}

	/// <inheritdoc />
	public Task<Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = Usuarios.FirstOrDefault(u => u.Id == usuarioId);

		return Task.FromResult(usuario is null
			? Result.Failure<UsuarioDetalheDto>(IntranetErrors.Acesso.UsuarioNaoEncontrado)
			: Result.Success(usuario));
	}

	/// <inheritdoc />
	public Task<Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Escrever($"perfil-criar:{nome}", () => ComPerfil(nome));

	/// <inheritdoc />
	public Task<Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default) =>
		Escrever($"perfil-excluir:{nome}", () => Perfis.RemoveAll(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase)));

	/// <inheritdoc />
	public Task<Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Escrever($"perfil-atribuir:{usuarioId}:{perfil}", () => Alterar(usuarioId, u => u with { Perfis = [.. u.Perfis, perfil] }));

	/// <inheritdoc />
	public Task<Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) =>
		Escrever($"perfil-retirar:{usuarioId}:{perfil}", () => Alterar(usuarioId, u => u with
		{
			Perfis = [.. u.Perfis.Where(p => !string.Equals(p, perfil, StringComparison.OrdinalIgnoreCase))],
		}));

	/// <inheritdoc />
	public Task<Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Escrever($"usuario-desativar:{usuarioId}", () => Alterar(usuarioId, u => u with { Situacao = SituacaoDoUsuario.Desativado }));

	/// <inheritdoc />
	public Task<Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Escrever($"usuario-reativar:{usuarioId}", () => Alterar(usuarioId, u => u with { Situacao = SituacaoDoUsuario.Ativo }));

	/// <inheritdoc />
	public Task<Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
		Escrever($"sessoes-encerrar:{usuarioId}", () => { });

	private List<MembroDoPerfilDto> MembrosDe(string nome) =>
		[.. Usuarios
			.Where(u => u.Perfis.Any(p => string.Equals(p, nome, StringComparison.OrdinalIgnoreCase)))
			.Select(u => new MembroDoPerfilDto(u.Id, u.Email, u.Situacao))];

	private void Alterar(Guid usuarioId, Func<UsuarioDetalheDto, UsuarioDetalheDto> mudanca)
	{
		var indice = Usuarios.FindIndex(u => u.Id == usuarioId);

		if (indice >= 0)
		{
			Usuarios[indice] = mudanca(Usuarios[indice]);
		}
	}

	private Task<Result> Escrever(string chamada, Action efeito)
	{
		Chamadas.Add(chamada);

		if (FalharCom is not null)
		{
			return Task.FromResult(Result.Failure(FalharCom));
		}

		efeito();

		return Task.FromResult(Result.Success());
	}
}
