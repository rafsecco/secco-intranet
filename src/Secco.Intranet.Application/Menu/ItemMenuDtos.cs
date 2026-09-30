using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Application.Menu;

/// <summary>Um nó da árvore, para leitura.</summary>
public sealed record ItemMenuDto(
	Guid Id, Guid? ParentId, string Nome, string Slug, TipoDeItemMenu Tipo, string? Rota, string? Icone, int Ordem, bool Ativo)
{
	/// <summary>Converte a entidade.</summary>
	public static ItemMenuDto FromEntity(ItemMenu item) =>
		new(item.Id, item.ParentId, item.Nome, item.Slug, item.Tipo, item.Rota, item.Icone, item.Ordem, item.Ativo);
}

/// <summary>Pedido de criação de um item.</summary>
/// <param name="SetorId">Setor dono da árvore.</param>
/// <param name="ParentId">Pai na árvore — nunca nulo pela tela (a raiz sempre existe e é uma opção).</param>
/// <param name="Nome">Rótulo.</param>
/// <param name="Slug">Identificador entre irmãos.</param>
/// <param name="Tipo">Tipo do item — a tela nunca envia <see cref="TipoDeItemMenu.Setor"/>.</param>
/// <param name="Rota">Só relevante em <see cref="TipoDeItemMenu.Personalizado"/>.</param>
/// <param name="Icone">Classe do Bootstrap Icons; vazio = sem ícone.</param>
public sealed record CriarItemMenuCommand(
	Guid SetorId, Guid ParentId, string? Nome, string? Slug, TipoDeItemMenu Tipo, string? Rota, string? Icone);
