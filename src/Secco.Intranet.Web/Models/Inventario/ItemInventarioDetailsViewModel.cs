using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Web.Models.Inventario;

/// <summary>Modelo da tela de detalhe: o item e os usuários do tenant, para o seletor de atribuição.</summary>
/// <param name="Item">Item de inventário.</param>
/// <param name="Usuarios">Usuários do tenant atual, para popular o seletor de atribuição.</param>
public sealed record ItemInventarioDetailsViewModel(ItemInventarioDto Item, IReadOnlyList<UsuarioDoTenant> Usuarios);
