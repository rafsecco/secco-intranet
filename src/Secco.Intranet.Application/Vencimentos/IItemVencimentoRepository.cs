using Secco.Intranet.Domain.Vencimentos;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Item elegível ao aviso automático, com o que o varredor precisa saber dele.</summary>
/// <param name="Item">O item de vencimento (não encerrado).</param>
/// <param name="SetorSlug">Slug do setor dono, usado para montar o link do aviso.</param>
/// <param name="MarcosTratados">Marcos já tratados no ciclo atual do item.</param>
public sealed record ItemParaAviso(ItemVencimento Item, string SetorSlug, IReadOnlySet<int> MarcosTratados);

/// <summary>Persistência de itens de vencimento, renovações e avisos tratados.</summary>
public interface IItemVencimentoRepository
{
	/// <summary>Grava um item novo.</summary>
	Task AddAsync(ItemVencimento item, CancellationToken ct = default);

	/// <summary>Busca o item rastreado, para alterar e chamar <see cref="SaveChangesAsync"/>.</summary>
	Task<ItemVencimento?> GetParaEdicaoAsync(Guid id, CancellationToken ct = default);

	/// <summary>Itens do setor ordenados por data de vencimento.</summary>
	/// <param name="setorId">Setor dono.</param>
	/// <param name="incluirEncerrados">Quando <c>false</c>, omite os encerrados.</param>
	/// <param name="ct">Token de cancelamento.</param>
	Task<IReadOnlyList<ItemVencimento>> ListarDoSetorAsync(Guid setorId, bool incluirEncerrados, CancellationToken ct = default);

	/// <summary>Renovações do item, a mais recente primeiro.</summary>
	Task<IReadOnlyList<RenovacaoDeVencimento>> ListarRenovacoesAsync(Guid itemId, CancellationToken ct = default);

	/// <summary>Avisos tratados do item, em todos os ciclos.</summary>
	Task<IReadOnlyList<AvisoDeVencimentoEnviado>> ListarAvisosAsync(Guid itemId, CancellationToken ct = default);

	/// <summary>
	/// Itens não encerrados de setor ativo cujo item de menu <c>Vencimentos</c> está ativo (com
	/// todos os ancestrais ativos), cada um com o conjunto de marcos já tratados no ciclo atual.
	/// </summary>
	Task<IReadOnlyList<ItemParaAviso>> ListarAtivosParaAvisoAsync(CancellationToken ct = default);

	/// <summary>
	/// Registra que um marco foi tratado. Devolve <c>false</c> quando o marco do ciclo já estava
	/// registrado (índice único) — outro processo chegou antes.
	/// </summary>
	Task<bool> TentarRegistrarAvisoAsync(AvisoDeVencimentoEnviado aviso, CancellationToken ct = default);

	/// <summary>Remove um registro de aviso (compensação quando o envio falha).</summary>
	Task RemoverAvisoAsync(Guid avisoId, CancellationToken ct = default);

	/// <summary>Grava o registro histórico de uma renovação.</summary>
	Task AdicionarRenovacaoAsync(RenovacaoDeVencimento renovacao, CancellationToken ct = default);

	/// <summary>Persiste as alterações dos itens rastreados.</summary>
	Task SaveChangesAsync(CancellationToken ct = default);
}
