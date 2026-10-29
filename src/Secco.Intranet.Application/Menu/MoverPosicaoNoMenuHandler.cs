using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Troca a posição de um item do menu com o vizinho e salva a ordem do grupo inteiro.</summary>
/// <param name="repository">Persistência das posições.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class MoverPosicaoNoMenuHandler(IPosicaoMenuRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso. Mover o primeiro para cima (ou o último para baixo) não faz nada — sucesso, sem efeito.</summary>
	/// <param name="grupo">Grupo do menu.</param>
	/// <param name="chavesNaOrdemEfetiva">Chaves do grupo como a tela as mostra agora.</param>
	/// <param name="chave">Item a mover.</param>
	/// <param name="paraCima"><c>true</c> troca com o anterior; <c>false</c>, com o próximo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		GrupoDoMenu grupo, IReadOnlyList<string> chavesNaOrdemEfetiva, string chave, bool paraCima,
		CancellationToken cancellationToken = default)
	{
		// O índice único (grupo, chave) não admite repetição: fica a primeira ocorrência.
		var chaves = chavesNaOrdemEfetiva.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		var indice = chaves.FindIndex(c => string.Equals(c, chave, StringComparison.OrdinalIgnoreCase));

		if (indice < 0)
		{
			return Result.Failure(IntranetErrors.Menu.PosicaoNotFound);
		}

		var indiceDoVizinho = paraCima ? indice - 1 : indice + 1;

		if (indiceDoVizinho < 0 || indiceDoVizinho >= chaves.Count)
		{
			return Result.Success();
		}

		(chaves[indice], chaves[indiceDoVizinho]) = (chaves[indiceDoVizinho], chaves[indice]);

		await repository.SubstituirGrupoAsync(grupo, chaves, cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.OrdemAsync(
			trilha, VerbosDeAuditoria.MenuOrdemMover, chave,
			new { grupo = grupo.ToString(), chave, de = indice, para = indiceDoVizinho },
			cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
