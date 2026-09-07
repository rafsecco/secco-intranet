using Secco.Intranet.Domain.Documentos;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Documento acompanhado dos dados do setor dono, para decidir acesso sem uma segunda consulta.</summary>
/// <param name="Documento">Entidade completa, com caminho e chave.</param>
/// <param name="SetorNome">Nome do setor dono.</param>
/// <param name="SetorSlug">Slug do setor dono — base da Role que autoriza a leitura (ADR-0001).</param>
public sealed record DocumentoComSetor(Documento Documento, string SetorNome, string SetorSlug);

/// <summary>Porta de persistência de documentos — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IDocumentoRepository
{
	/// <summary>Persiste um documento.</summary>
	/// <param name="documento">Documento a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(Documento documento, CancellationToken cancellationToken = default);

	/// <summary>Grava as alterações pendentes de um documento já rastreado.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);

	/// <summary>Busca um documento pelo identificador, junto do setor dono.</summary>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<DocumentoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Lista os documentos ativos de um setor, do mais recente para o mais antigo.</summary>
	/// <param name="setorSlug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<DocumentoDto>> ListarPorSetorAsync(string setorSlug, CancellationToken cancellationToken = default);
}
