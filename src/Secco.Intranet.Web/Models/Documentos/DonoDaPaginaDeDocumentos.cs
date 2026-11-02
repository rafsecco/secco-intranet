namespace Secco.Intranet.Web.Models.Documentos;

/// <summary>
/// Quem é dono da página de documentos (setor ou biblioteca), reduzido ao que as views
/// compartilhadas precisam — elas não conhecem <c>SetorDto</c> nem <c>BibliotecaDto</c>.
/// </summary>
/// <param name="Nome">Nome de exibição do dono.</param>
/// <param name="Slug">Slug do dono, valor de rota <c>slug</c> das ações da página.</param>
/// <param name="Icone">Classe do Bootstrap Icons do dono, quando houver.</param>
/// <param name="MostraVisibilidade">Se os formulários oferecem "Quem pode ver": só o setor tem visibilidade.</param>
public sealed record DonoDaPaginaDeDocumentos(string Nome, string Slug, string? Icone, bool MostraVisibilidade);
