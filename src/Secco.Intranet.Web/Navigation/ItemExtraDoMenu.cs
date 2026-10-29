using Secco.Intranet.Web.Extensibility;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Item extra do menu principal, já liberado pelo gate e sem a lógica de visibilidade.</summary>
/// <param name="Chave">Chave estável do item (também a chave de posição).</param>
/// <param name="Rotulo">Texto exibido.</param>
/// <param name="Icone">Classe do ícone, se houver.</param>
/// <param name="Url">Caminho local do destino.</param>
/// <param name="Ordem">Ordem padrão entre os extras.</param>
public sealed record ItemExtraDoMenu(string Chave, string Rotulo, string? Icone, string Url, int Ordem)
{
	/// <summary>Copia os dados do item de um projeto externo.</summary>
	/// <param name="item">Item descoberto no catálogo.</param>
	public static ItemExtraDoMenu De(IItemDeMenuPrincipal item)
	{
		ArgumentNullException.ThrowIfNull(item);

		return new ItemExtraDoMenu(item.Chave, item.Rotulo, item.Icone, item.Url, item.Ordem);
	}
}

/// <summary>Entrada do grupo principal (fixa ou extra), na ordem padrão ou na ordem do tenant.</summary>
/// <param name="Chave">Chave de posição.</param>
/// <param name="Rotulo">Texto exibido.</param>
/// <param name="Icone">Classe do ícone, se houver.</param>
/// <param name="Url">Caminho do destino.</param>
public sealed record EntradaDoMenuPrincipal(string Chave, string Rotulo, string? Icone, string Url);
