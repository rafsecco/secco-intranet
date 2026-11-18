using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Um departamento do Entra que não casa com nenhum setor da intranet.</summary>
/// <param name="Departamento">Primeira grafia encontrada.</param>
/// <param name="Pessoas">Quantas pessoas ativas têm esse departamento.</param>
/// <param name="Ambiguo">Se o nome casa com mais de um setor ativo (por isso não vincula).</param>
public sealed record DepartamentoSemSetorDto(string Departamento, int Pessoas, bool Ambiguo);

/// <summary>Lista os departamentos do Entra sem setor correspondente, para o admin criar ou renomear setores.</summary>
/// <param name="usuarios">Fonte de identidade, com os atributos do Entra.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class ListarDepartamentosSemSetorHandler(IUsuariosParaDiretorio usuarios, ISetorRepository setores)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<DepartamentoSemSetorDto>>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<IReadOnlyList<DepartamentoSemSetorDto>>(ativos.Error);
		}

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);

		// Ordem de entrada: a primeira grafia vista de cada grupo é a exibida.
		var grupos = new Dictionary<string, (string Grafia, int Pessoas)>(StringComparer.Ordinal);

		foreach (var usuario in ativos.Value)
		{
			var departamento = usuario.Entra?.Departamento;

			if (string.IsNullOrWhiteSpace(departamento))
			{
				continue;
			}

			var chave = CasadorDeDepartamento.Normalizar(departamento);

			grupos[chave] = grupos.TryGetValue(chave, out var atual)
				? (atual.Grafia, atual.Pessoas + 1)
				: (departamento.Trim(), 1);
		}

		IReadOnlyList<DepartamentoSemSetorDto> semSetor =
		[
			.. grupos.Values
				.Where(grupo => CasadorDeDepartamento.Casar(grupo.Grafia, todosOsSetores) is null)
				.Select(grupo => new DepartamentoSemSetorDto(
					grupo.Grafia, grupo.Pessoas, CasadorDeDepartamento.EhAmbiguo(grupo.Grafia, todosOsSetores)))
				.OrderBy(dto => dto.Departamento, StringComparer.OrdinalIgnoreCase),
		];

		return Result.Success(semSetor);
	}
}
