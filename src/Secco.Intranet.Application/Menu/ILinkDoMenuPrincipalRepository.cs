using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Porta de persistência dos links do menu principal — sempre no banco do tenant atual (ADR-0005).</summary>
public interface ILinkDoMenuPrincipalRepository
{
	/// <summary>Todos os links, desrastreados.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<LinkDoMenuPrincipal>> ListarAsync(CancellationToken cancellationToken = default);

	/// <summary>Obtém um link rastreado; nulo se não existir.</summary>
	/// <param name="id">Id do link.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<LinkDoMenuPrincipal?> ObterAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Grava um link novo.</summary>
	/// <param name="link">Link a gravar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(LinkDoMenuPrincipal link, CancellationToken cancellationToken = default);

	/// <summary>Persiste as alterações de um link obtido por <see cref="ObterAsync"/>.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SalvarAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Remove o link e a posição do grupo <see cref="GrupoDoMenu.Principal"/> com a mesma chave,
	/// num único <c>SaveChangesAsync</c>.
	/// </summary>
	/// <param name="link">Link a remover.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoverAsync(LinkDoMenuPrincipal link, CancellationToken cancellationToken = default);
}
