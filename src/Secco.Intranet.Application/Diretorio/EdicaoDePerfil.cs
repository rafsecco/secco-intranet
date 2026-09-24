using Secco.Intranet.Domain.Diretorio;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>
/// Aplica uma edição a um perfil, criando-o na primeira vez. Não persiste um perfil que a edição
/// deixaria vazio, e contorna a corrida de dois primeiros salvamentos (o índice único barra o
/// segundo; o repositório avisa e este método reaplica sobre o perfil que o outro criou).
/// </summary>
internal static class EdicaoDePerfil
{
	/// <summary>Devolve os nomes dos campos que de fato mudaram (vazio = nada foi gravado).</summary>
	public static async Task<IReadOnlyList<string>> AplicarAsync(
		IPerfilColaboradorRepository repositorio,
		Guid usuarioId,
		Func<PerfilColaborador, IReadOnlyList<string>> editar,
		CancellationToken cancellationToken)
	{
		var existente = await repositorio.GetParaEdicaoAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (existente is not null)
		{
			return await AplicarNoExistenteAsync(repositorio, existente, editar, cancellationToken).ConfigureAwait(false);
		}

		var novo = new PerfilColaborador(usuarioId);
		var campos = editar(novo);

		if (campos.Count == 0)
		{
			return campos;
		}

		if (await repositorio.TentarAdicionarAsync(novo, cancellationToken).ConfigureAwait(false))
		{
			return campos;
		}

		var concorrente = await repositorio.GetParaEdicaoAsync(usuarioId, cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException("O perfil que impediu a criação não foi encontrado.");

		return await AplicarNoExistenteAsync(repositorio, concorrente, editar, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<IReadOnlyList<string>> AplicarNoExistenteAsync(
		IPerfilColaboradorRepository repositorio,
		PerfilColaborador perfil,
		Func<PerfilColaborador, IReadOnlyList<string>> editar,
		CancellationToken cancellationToken)
	{
		var campos = editar(perfil);

		if (campos.Count > 0)
		{
			await repositorio.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return campos;
	}
}
