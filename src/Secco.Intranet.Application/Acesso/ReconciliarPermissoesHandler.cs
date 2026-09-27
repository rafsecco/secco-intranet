using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Quantos setores e quantos perfis do Diretório tiveram permissão garantida.</summary>
public sealed record ReconciliacaoResumo(int Setores, int PerfisDoDiretorio);

/// <summary>
/// Reconcilia permissão de setor e do Diretório (ADR-0021): cobre setores cadastrados antes deste
/// modelo existir, sem nenhuma permissão gravada, e a migração do Diretório de nome de Role para
/// permissão. Idempotente — mesclagem (<see cref="IGestaoDeAcesso.GarantirPermissoesAsync"/>),
/// nunca apaga nada.
/// </summary>
/// <param name="searchSetores">Busca de setores já existente (a mesma do menu).</param>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class ReconciliarPermissoesHandler(SearchSetoresHandler searchSetores, IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	private const int TamanhoDaPagina = 100;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ReconciliacaoResumo>> HandleAsync(CancellationToken cancellationToken = default)
	{
		var totalSetores = 0;
		var pagina = PageRequest.FirstPage;

		while (true)
		{
			var busca = await searchSetores
				.HandleAsync(new SetorSearchCriteria(Page: new PageRequest(pagina, TamanhoDaPagina)), cancellationToken)
				.ConfigureAwait(false);

			if (busca.IsFailure)
			{
				return Result.Failure<ReconciliacaoResumo>(busca.Error);
			}

			foreach (var setor in busca.Value.Items)
			{
				await gestao.GarantirPermissoesAsync(
					$"{setor.Slug}-user", [IntranetPermissoes.Setor.Read(setor.Slug)], cancellationToken).ConfigureAwait(false);
				await gestao.GarantirPermissoesAsync(
					$"{setor.Slug}-admin",
					[IntranetPermissoes.Setor.Read(setor.Slug), IntranetPermissoes.Setor.Write(setor.Slug)],
					cancellationToken).ConfigureAwait(false);

				totalSetores++;
			}

			if (!busca.Value.HasNextPage)
			{
				break;
			}

			pagina++;
		}

		var perfis = await gestao.ListarPerfisAsync(cancellationToken).ConfigureAwait(false);
		var totalDiretorio = 0;

		if (perfis.IsSuccess)
		{
			var nomes = perfis.Value.Select(p => p.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);

			if (nomes.Contains(ClassificacaoDePerfil.DiretorioUsuario))
			{
				await gestao.GarantirPermissoesAsync(
					ClassificacaoDePerfil.DiretorioUsuario, [IntranetPermissoes.Diretorio.Read], cancellationToken).ConfigureAwait(false);
				totalDiretorio++;
			}

			if (nomes.Contains(ClassificacaoDePerfil.DiretorioAdmin))
			{
				await gestao.GarantirPermissoesAsync(
					ClassificacaoDePerfil.DiretorioAdmin,
					[IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Diretorio.Manage],
					cancellationToken).ConfigureAwait(false);
				totalDiretorio++;
			}
		}

		await AuditoriaDeAcesso.ReconciliacaoAsync(trilha, totalSetores, totalDiretorio, cancellationToken).ConfigureAwait(false);

		return Result.Success(new ReconciliacaoResumo(totalSetores, totalDiretorio));
	}
}
