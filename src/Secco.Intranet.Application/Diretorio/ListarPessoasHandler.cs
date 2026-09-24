using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Pedido da grade de pessoas.</summary>
/// <param name="Busca">Trecho de nome, cargo, setor ou e-mail.</param>
/// <param name="SetorSlug">Restringe à lotação neste setor.</param>
/// <param name="Pagina">Página (1-based), 24 por página.</param>
public sealed record ListarPessoasQuery(string? Busca, string? SetorSlug, int Pagina);

/// <summary>Grade de pessoas e os setores ativos para o filtro.</summary>
/// <param name="Pagina">Página de pessoas.</param>
/// <param name="Setores">Setores ativos, para o filtro.</param>
public sealed record PessoasDaTelaDto(PagedResult<PessoaDto> Pagina, IReadOnlyList<SetorDto> Setores);

/// <summary>Lista pessoas: usuários ativos do SecureGate + perfil local, filtrados e paginados em memória.</summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class ListarPessoasHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores)
{
	private const int TamanhoDaPagina = 24;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Filtros e página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PessoasDaTelaDto>> HandleAsync(ListarPessoasQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<PessoasDaTelaDto>(ativos.Error);
		}

		var todosOsSetores = await CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var pessoas = MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores);

		var busca = query.Busca?.Trim();
		var slug = query.SetorSlug?.Trim();

		var filtradas = pessoas
			.Where(pessoa => string.IsNullOrEmpty(slug)
				|| string.Equals(pessoa.SetorSlug, slug, StringComparison.OrdinalIgnoreCase))
			.Where(pessoa => string.IsNullOrEmpty(busca) || Corresponde(pessoa, busca))
			.OrderBy(pessoa => pessoa.Nome, StringComparer.OrdinalIgnoreCase)
			.ThenBy(pessoa => pessoa.UsuarioId)
			.ToList();

		var pagina = new PageRequest(Math.Max(1, query.Pagina), TamanhoDaPagina);
		IReadOnlyList<PessoaDto> itens = [.. filtradas.Skip(pagina.Skip).Take(pagina.Size)];
		IReadOnlyList<SetorDto> ativosParaFiltro = [.. todosOsSetores.Where(setor => setor.Ativo).OrderBy(setor => setor.Nome, StringComparer.OrdinalIgnoreCase)];

		return new PessoasDaTelaDto(PagedResult.Create(itens, pagina, filtradas.Count), ativosParaFiltro);
	}

	internal static async Task<IReadOnlyList<SetorDto>> CarregarSetoresAsync(ISetorRepository setores, CancellationToken cancellationToken)
	{
		// O repositório limita a página a 200; um tenant com mais setores que isso não é o caso de uso.
		var pagina = await setores
			.SearchAsync(new SetorSearchCriteria(ApenasAtivos: false, Page: new PageRequest(1, 200)), cancellationToken)
			.ConfigureAwait(false);

		return [.. pagina.Items.Select(SetorDto.FromEntity)];
	}

	private static bool Corresponde(PessoaDto pessoa, string busca) =>
		Contem(pessoa.Nome, busca) || Contem(pessoa.Cargo, busca) || Contem(pessoa.SetorNome, busca) || Contem(pessoa.Email, busca);

	private static bool Contem(string? texto, string trecho) =>
		texto is not null && texto.Contains(trecho, StringComparison.OrdinalIgnoreCase);
}
