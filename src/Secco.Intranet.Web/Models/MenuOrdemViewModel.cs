using Secco.Intranet.Application.Menu;

namespace Secco.Intranet.Web.Models;

/// <summary>Modelo da tela de ordenação do menu: as duas listas na ordem efetiva do tenant.</summary>
/// <param name="Principal">Itens do grupo principal (Mural, Diretório, bibliotecas, extras e links).</param>
/// <param name="Setores">Setores ativos.</param>
/// <param name="Links">Links do menu principal, para o card de gestão.</param>
public sealed record MenuOrdemViewModel(
	IReadOnlyList<LinhaDaOrdem> Principal,
	IReadOnlyList<LinhaDaOrdem> Setores,
	IReadOnlyList<LinkDoMenuPrincipalDto> Links);

/// <summary>Uma linha da lista ordenável.</summary>
/// <param name="Chave">Chave de posição do item.</param>
/// <param name="Rotulo">Nome mostrado.</param>
/// <param name="Icone">Classe do ícone, quando houver.</param>
public sealed record LinhaDaOrdem(string Chave, string Rotulo, string? Icone);
