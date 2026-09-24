using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido do detalhe de um perfil.</summary>
/// <param name="Nome">Nome do perfil.</param>
/// <param name="Pagina">Página de membros (1-based).</param>
public sealed record ObterPerfilQuery(string Nome, int Pagina);

/// <summary>Tela de detalhe de um perfil.</summary>
/// <param name="Perfil">Detalhe do perfil, com permissões só para leitura.</param>
/// <param name="Membros">Página de membros.</param>
/// <param name="Candidatos">Usuários ativos que ainda não têm o perfil; vazio se o perfil é reservado.</param>
public sealed record PerfilTelaDto(PerfilDetalheDto Perfil, PaginaDeMembros Membros, IReadOnlyList<UsuarioDto> Candidatos);

/// <summary>Detalhe de um perfil, com membros e candidatos a membro.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
public sealed class ObterPerfilHandler(IGestaoDeAcesso gestao)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Perfil e página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PerfilTelaDto>> HandleAsync(ObterPerfilQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var perfil = await gestao.ObterPerfilAsync(query.Nome, cancellationToken).ConfigureAwait(false);

		if (perfil.IsFailure)
		{
			return Result.Failure<PerfilTelaDto>(perfil.Error);
		}

		var membros = await gestao
			.ListarMembrosAsync(perfil.Value.Nome, Math.Max(1, query.Pagina), cancellationToken)
			.ConfigureAwait(false);

		if (membros.IsFailure)
		{
			return Result.Failure<PerfilTelaDto>(membros.Error);
		}

		IReadOnlyList<UsuarioDto> candidatos = [];

		if (!perfil.Value.Reservado && !ClassificacaoDePerfil.EhReservado(perfil.Value.Nome))
		{
			var usuarios = await gestao.ListarUsuariosAsync(cancellationToken).ConfigureAwait(false);

			if (usuarios.IsFailure)
			{
				return Result.Failure<PerfilTelaDto>(usuarios.Error);
			}

			candidatos =
			[
				.. usuarios.Value
					.Where(usuario => usuario.Situacao == SituacaoDoUsuario.Ativo
						&& !usuario.Perfis.Any(p => string.Equals(p, perfil.Value.Nome, StringComparison.OrdinalIgnoreCase)))
					.OrderBy(usuario => usuario.Email, StringComparer.OrdinalIgnoreCase),
			];
		}

		return new PerfilTelaDto(perfil.Value, membros.Value, candidatos);
	}
}
