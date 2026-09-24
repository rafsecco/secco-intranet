using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>
/// A plataforma não conhece <c>intranet-admin</c> e deixa tirar o último — o que trancaria
/// todo mundo para fora da própria área administrativa. Esta regra é da Intranet.
/// </summary>
internal static class RegrasDeIntranetAdmin
{
	// Teto de segurança do laço: 50 páginas de 100 são 5 mil intranet-admins. Se chegar lá,
	// assume que sobra alguém — errar para o lado de não bloquear.
	private const int LimiteDePaginas = 50;

	/// <summary>Indica se, tirando <paramref name="usuarioId"/>, ainda sobra algum <c>intranet-admin</c> ativo.</summary>
	/// <param name="gestao">Porta de gestão de acesso.</param>
	/// <param name="usuarioId">Usuário que deixaria de ser intranet-admin (ou ativo).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<Result<bool>> RestamOutrosAtivosAsync(
		IGestaoDeAcesso gestao, Guid usuarioId, CancellationToken cancellationToken)
	{
		for (var pagina = 1; pagina <= LimiteDePaginas; pagina++)
		{
			var lida = await gestao
				.ListarMembrosAsync(ClassificacaoDePerfil.IntranetAdmin, pagina, cancellationToken)
				.ConfigureAwait(false);

			if (lida.IsFailure)
			{
				return Result.Failure<bool>(lida.Error);
			}

			if (lida.Value.Itens.Any(m => m.UsuarioId != usuarioId && m.Situacao == SituacaoDoUsuario.Ativo))
			{
				return true;
			}

			if (pagina >= lida.Value.TotalDePaginas)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Indica se o ator identificado é o próprio usuário-alvo.</summary>
	/// <param name="ator">Ator atual (pode não haver usuário identificado no modo aberto de DEV).</param>
	/// <param name="usuarioId">Usuário-alvo.</param>
	public static bool EhOProprio(IAtorAtual ator, Guid usuarioId) =>
		ator.Atual() is { } atual && Guid.TryParse(atual.Id, out var id) && id == usuarioId;
}
