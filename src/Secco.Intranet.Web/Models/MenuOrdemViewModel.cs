namespace Secco.Intranet.Web.Models;

/// <summary>Modelo da tela de ordenação do menu: as duas listas na ordem efetiva do tenant.</summary>
/// <param name="Principal">Itens do grupo principal (Mural, Diretório e extras).</param>
/// <param name="Setores">Setores ativos.</param>
public sealed record MenuOrdemViewModel(IReadOnlyList<LinhaDaOrdem> Principal, IReadOnlyList<LinhaDaOrdem> Setores);

/// <summary>Uma linha da lista ordenável.</summary>
/// <param name="Chave">Chave de posição do item.</param>
/// <param name="Rotulo">Nome mostrado.</param>
/// <param name="Icone">Classe do ícone, quando houver.</param>
public sealed record LinhaDaOrdem(string Chave, string Rotulo, string? Icone);
