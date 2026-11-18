using Secco.Intranet.Application.Diretorio;

namespace Secco.Intranet.Application.Salas;

/// <summary>Lista as pessoas que podem ser convidadas: os ativos do Diretório, por nome, sem quem pede.</summary>
public sealed class ListarConvidadosPossiveisHandler(IUsuariosParaDiretorio diretorio)
{
	/// <summary>Executa o caso de uso; se o Diretório falha, a lista vem vazia.</summary>
	/// <param name="quemPede">Usuário que pede, excluído da lista.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyList<PessoaDaReservaDto>> HandleAsync(Guid quemPede, CancellationToken cancellationToken = default)
	{
		var ativos = await diretorio.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return [];
		}

		return
		[
			.. ativos.Value
				.Where(usuario => usuario.Id != quemPede)
				.Select(usuario => new PessoaDaReservaDto(usuario.Id, ApoioDeReservas.Nome(usuario)))
				.OrderBy(pessoa => pessoa.Nome, StringComparer.CurrentCultureIgnoreCase),
		];
	}
}
