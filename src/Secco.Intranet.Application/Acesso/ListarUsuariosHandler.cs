using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Pedido da aba "Usuários".</summary>
/// <param name="Busca">Trecho do e-mail (opcional).</param>
/// <param name="Pagina">Página (1-based).</param>
public sealed record ListarUsuariosQuery(string? Busca, int Pagina);

/// <summary>
/// Lista usuários com busca e paginação em memória: <c>ListUsers</c> do SecureGate devolve o
/// tenant inteiro de uma vez, e paginar é da tela.
/// </summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
public sealed class ListarUsuariosHandler(IGestaoDeAcesso gestao)
{
	private const int TamanhoDaPagina = 20;

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Busca e página.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<UsuarioDto>>> HandleAsync(
		ListarUsuariosQuery query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var lidos = await gestao.ListarUsuariosAsync(cancellationToken).ConfigureAwait(false);

		if (lidos.IsFailure)
		{
			return Result.Failure<PagedResult<UsuarioDto>>(lidos.Error);
		}

		var busca = query.Busca?.Trim();

		var filtrados = lidos.Value
			.Where(usuario => string.IsNullOrEmpty(busca)
				|| (usuario.Email ?? string.Empty).Contains(busca, StringComparison.OrdinalIgnoreCase))
			.OrderBy(usuario => usuario.Email, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var pagina = new PageRequest(Math.Max(1, query.Pagina), TamanhoDaPagina);
		IReadOnlyList<UsuarioDto> itens = [.. filtrados.Skip(pagina.Skip).Take(pagina.Size)];

		return PagedResult.Create(itens, pagina, filtrados.Count);
	}
}
