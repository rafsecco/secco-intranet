using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Web.Models;

/// <summary>Modelo da tela de administração da árvore de itens de um setor.</summary>
/// <param name="Setor">O setor dono da árvore.</param>
/// <param name="Raiz">A árvore inteira, aninhada a partir da raiz.</param>
public sealed record SetorMenuViewModel(SetorDto Setor, NoDaArvoreDto Raiz);
