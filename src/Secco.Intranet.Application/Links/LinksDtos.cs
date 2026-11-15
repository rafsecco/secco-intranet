namespace Secco.Intranet.Application.Links;

/// <summary>Link do setor.</summary>
/// <param name="Id">Identificador do item de menu.</param>
/// <param name="Nome">Rótulo.</param>
/// <param name="Rota">Endereço de destino.</param>
/// <param name="NovaAba">Abre em nova aba.</param>
/// <param name="GrupoId">Grupo em que está; nulo quando fica solto sob o nó de links.</param>
public sealed record LinkDto(Guid Id, string Nome, string Rota, bool NovaAba, Guid? GrupoId);

/// <summary>Grupo de links e seus links.</summary>
/// <param name="Id">Identificador do item de menu.</param>
/// <param name="Nome">Rótulo.</param>
/// <param name="Links">Links do grupo, por ordem.</param>
public sealed record GrupoDeLinksDto(Guid Id, string Nome, IReadOnlyList<LinkDto> Links);

/// <summary>Entrada da lista: um grupo OU um link solto — se misturam pela ordem do menu.</summary>
/// <param name="Grupo">O grupo, quando a entrada é um grupo.</param>
/// <param name="Link">O link, quando a entrada é um link solto.</param>
public sealed record EntradaDeLinksDto(GrupoDeLinksDto? Grupo, LinkDto? Link);

/// <summary>Os links de um setor.</summary>
/// <param name="Entradas">Na ordem do menu.</param>
/// <param name="Grupos">Todos os grupos, para o seletor do formulário.</param>
public sealed record LinksDoSetorDto(IReadOnlyList<EntradaDeLinksDto> Entradas, IReadOnlyList<GrupoDeLinksDto> Grupos);

/// <summary>Dados de um link novo.</summary>
/// <param name="SetorId">Setor da tela.</param>
/// <param name="Nome">Rótulo.</param>
/// <param name="Rota">Endereço de destino.</param>
/// <param name="NovaAba">Abre em nova aba.</param>
/// <param name="GrupoId">Grupo de destino; nulo = solto sob o nó de links.</param>
public sealed record CriarLinkCommand(Guid SetorId, string? Nome, string? Rota, bool NovaAba, Guid? GrupoId);

/// <summary>Novos dados de um link ou grupo; num grupo só <paramref name="Nome"/> vale.</summary>
/// <param name="SetorId">Setor da tela.</param>
/// <param name="Id">Item a editar.</param>
/// <param name="Nome">Rótulo.</param>
/// <param name="Rota">Endereço de destino.</param>
/// <param name="NovaAba">Abre em nova aba.</param>
/// <param name="GrupoId">Grupo de destino; nulo = solto sob o nó de links.</param>
public sealed record EditarLinkCommand(Guid SetorId, Guid Id, string? Nome, string? Rota, bool NovaAba, Guid? GrupoId);
