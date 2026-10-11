using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>Tela de um perfil do tenant administrado.</summary>
/// <param name="Perfil">Detalhe.</param>
/// <param name="Membros">Página de membros.</param>
/// <param name="Candidatos">Pessoas ativas que ainda não têm o perfil.</param>
public sealed record PerfilDoTenantTelaDto(PerfilDoTenantDetalheDto Perfil, PaginaDeMembros Membros, IReadOnlyList<UsuarioDto> Candidatos);

/// <summary>Tela de uma pessoa do tenant administrado.</summary>
/// <param name="Usuario">Detalhe.</param>
/// <param name="PerfisAtribuiveis">Perfis que ela ainda não tem, sem reservados.</param>
public sealed record UsuarioDoTenantTelaDto(UsuarioDoTenantDetalheDto Usuario, IReadOnlyList<string> PerfisAtribuiveis);

/// <summary>Leituras do acesso de um tenant administrado. Só as regras genéricas — nenhuma convenção da Intranet.</summary>
/// <param name="gestao">Porta.</param>
public sealed class LeituraDoAcessoDoTenantHandler(IGestaoDeAcessoDoTenant gestao)
{
	private const int TamanhoDaPagina = 20;

	/// <summary>Todos os perfis, reservados inclusive (a lista os sinaliza).</summary>
	public Task<Result<IReadOnlyList<PerfilDoTenantDto>>> ListarPerfisAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		gestao.ListarPerfisAsync(tenantId, cancellationToken);

	/// <summary>Nomes dos perfis oferecíveis em seletor — sem reservados, em ordem alfabética.</summary>
	public async Task<Result<IReadOnlyList<string>>> PerfisAtribuiveisAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var perfis = await gestao.ListarPerfisAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return perfis.IsFailure
			? Result.Failure<IReadOnlyList<string>>(perfis.Error)
			: Result.Success<IReadOnlyList<string>>(
				[.. perfis.Value.Where(p => !p.Reservado).Select(p => p.Nome).Order(StringComparer.OrdinalIgnoreCase)]);
	}

	/// <summary>Detalhe de um perfil com membros e candidatos.</summary>
	public async Task<Result<PerfilDoTenantTelaDto>> ObterPerfilAsync(
		Guid tenantId, string nome, int pagina, CancellationToken cancellationToken = default)
	{
		var perfil = await gestao.ObterPerfilAsync(tenantId, nome, cancellationToken).ConfigureAwait(false);

		if (perfil.IsFailure)
		{
			return Result.Failure<PerfilDoTenantTelaDto>(perfil.Error);
		}

		var membros = await gestao.ListarMembrosAsync(tenantId, perfil.Value.Nome, Math.Max(1, pagina), cancellationToken).ConfigureAwait(false);

		if (membros.IsFailure)
		{
			return Result.Failure<PerfilDoTenantTelaDto>(membros.Error);
		}

		var usuarios = await gestao.ListarUsuariosAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (usuarios.IsFailure)
		{
			return Result.Failure<PerfilDoTenantTelaDto>(usuarios.Error);
		}

		IReadOnlyList<UsuarioDto> candidatos =
		[
			.. usuarios.Value
				.Where(u => u.Situacao == SituacaoDoUsuario.Ativo && !u.Perfis.Contains(perfil.Value.Nome, StringComparer.Ordinal))
				.OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase),
		];

		return Result.Success(new PerfilDoTenantTelaDto(perfil.Value, membros.Value, candidatos));
	}

	/// <summary>Pessoas com busca por e-mail e paginação em memória (a plataforma devolve o tenant inteiro).</summary>
	public async Task<Result<PagedResult<UsuarioDto>>> ListarUsuariosAsync(
		Guid tenantId, string? busca, int pagina, CancellationToken cancellationToken = default)
	{
		var lidos = await gestao.ListarUsuariosAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (lidos.IsFailure)
		{
			return Result.Failure<PagedResult<UsuarioDto>>(lidos.Error);
		}

		var termo = busca?.Trim();
		var filtrados = lidos.Value
			.Where(u => string.IsNullOrEmpty(termo) || (u.Email ?? string.Empty).Contains(termo, StringComparison.OrdinalIgnoreCase))
			.OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var pedido = new PageRequest(Math.Max(1, pagina), TamanhoDaPagina);
		IReadOnlyList<UsuarioDto> itens = [.. filtrados.Skip(pedido.Skip).Take(pedido.Size)];

		return PagedResult.Create(itens, pedido, filtrados.Count);
	}

	/// <summary>Detalhe de uma pessoa com os perfis que ainda pode receber.</summary>
	public async Task<Result<UsuarioDoTenantTelaDto>> ObterUsuarioAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure<UsuarioDoTenantTelaDto>(usuario.Error);
		}

		var atribuiveis = await PerfisAtribuiveisAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (atribuiveis.IsFailure)
		{
			return Result.Failure<UsuarioDoTenantTelaDto>(atribuiveis.Error);
		}

		IReadOnlyList<string> restantes = [.. atribuiveis.Value.Where(p => !usuario.Value.Perfis.Contains(p, StringComparer.Ordinal))];

		return Result.Success(new UsuarioDoTenantTelaDto(usuario.Value, restantes));
	}

	/// <summary>Federação do tenant.</summary>
	public Task<Result<FederacaoDto>> ObterFederacaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		gestao.ObterFederacaoAsync(tenantId, cancellationToken);
}
