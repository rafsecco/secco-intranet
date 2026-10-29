using System.Security.Claims;
using Secco.Intranet.Web.Extensibility;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Avalia os gates dos itens extras do menu principal.</summary>
public static class GatesDoMenuPrincipal
{
	/// <summary>
	/// Devolve os itens que o usuário pode ver. No modo aberto de DEV todos passam, sem chamar o gate;
	/// um gate que lança exceção tira só o seu item, com um aviso.
	/// </summary>
	/// <param name="itens">Itens válidos do catálogo.</param>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="modoAberto">Modo aberto de DEV.</param>
	/// <param name="logger">Log dos avisos.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<IReadOnlyList<ItemExtraDoMenu>> FiltrarAsync(
		IReadOnlyList<IItemDeMenuPrincipal> itens,
		ClaimsPrincipal usuario,
		bool modoAberto,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(itens);
		ArgumentNullException.ThrowIfNull(logger);

		var visiveis = new List<ItemExtraDoMenu>();

		foreach (var item in itens)
		{
			try
			{
				if (modoAberto || await item.VisivelParaAsync(usuario, cancellationToken).ConfigureAwait(false))
				{
					visiveis.Add(ItemExtraDoMenu.De(item));
				}
			}
#pragma warning disable CA1031 // Gate de terceiro nunca derruba a página.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				logger.LogWarning(exception, "O gate do item de menu {Chave} lançou exceção; o item ficou fora do menu.", item.Chave);
			}
		}

		return visiveis;
	}
}
