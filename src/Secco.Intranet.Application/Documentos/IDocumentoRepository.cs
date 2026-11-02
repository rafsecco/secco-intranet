using Secco.Intranet.Domain.Documentos;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Documento acompanhado dos dados do dono, para decidir acesso sem uma segunda consulta.</summary>
/// <param name="Documento">Entidade completa, com caminho e chave.</param>
/// <param name="Dono">Referência ao dono (setor ou biblioteca) — base da permissão que autoriza a leitura.</param>
/// <param name="DonoNome">Nome do dono.</param>
/// <param name="DonoAtivo">Se o dono está ativo; dono inativo não entrega documentos.</param>
public sealed record DocumentoComDono(Documento Documento, ReferenciaDeDono Dono, string DonoNome, bool DonoAtivo);

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

	/// <summary>Busca um documento pelo identificador, junto do dono.</summary>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<DocumentoComDono?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Lista os documentos ativos de um dono, por <c>Ordem</c>; filhos e raiz vêm na mesma lista plana.</summary>
	/// <param name="dono">Referência ao setor ou à biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<DocumentoDto>> ListarPorDonoAsync(ReferenciaDeDono dono, CancellationToken cancellationToken = default);

	/// <summary>Lista os itens ativos de um mesmo pai, rastreados, por <c>Ordem</c>.</summary>
	/// <param name="dono">Dono dos irmãos.</param>
	/// <param name="parentId">Pai dos irmãos; <c>null</c> para a raiz.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<Documento>> ListarIrmaosAsync(
		DonoDeDocumento dono,
		Guid? parentId,
		CancellationToken cancellationToken = default);

	/// <summary>Indica se o documento tem filhos ativos.</summary>
	/// <param name="documentoId">Identificador do possível pai.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> TemFilhosAtivosAsync(Guid documentoId, CancellationToken cancellationToken = default);
}
