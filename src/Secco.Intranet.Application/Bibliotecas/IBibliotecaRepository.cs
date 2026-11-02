using Secco.Intranet.Domain.Bibliotecas;

namespace Secco.Intranet.Application.Bibliotecas;

/// <summary>Porta de persistência de bibliotecas de documentos — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IBibliotecaRepository
{
	/// <summary>Persiste uma biblioteca.</summary>
	/// <param name="biblioteca">Biblioteca a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(Biblioteca biblioteca, CancellationToken cancellationToken = default);

	/// <summary>Grava alterações pendentes de uma biblioteca já rastreada.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Busca uma biblioteca <b>rastreada</b> pelo identificador, para alteração: alterar o
	/// resultado e chamar <see cref="SaveChangesAsync"/> grava de fato.
	/// </summary>
	/// <param name="id">Identificador da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Biblioteca?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Busca uma biblioteca pelo slug, sem diferenciar maiúsculas de minúsculas.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Biblioteca?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

	/// <summary>Verifica se já existe uma biblioteca com o slug informado, sem diferenciar caixa.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);

	/// <summary>Lista as bibliotecas por nome.</summary>
	/// <param name="somenteAtivas">Quando <c>true</c>, omite as desativadas.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<Biblioteca>> ListarAsync(bool somenteAtivas, CancellationToken cancellationToken = default);
}
