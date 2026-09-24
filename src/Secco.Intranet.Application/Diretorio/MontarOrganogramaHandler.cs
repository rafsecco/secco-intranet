using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Monta o organograma do tenant a partir dos usuários ativos e dos perfis locais.</summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class MontarOrganogramaHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<OrganogramaDto>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<OrganogramaDto>(ativos.Error);
		}

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);

		return ConstrutorDeOrganograma.Construir(MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores));
	}
}
