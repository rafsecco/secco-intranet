namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Documentos de um setor em árvore de dois níveis. O tema decide como ela parece; o core decide o que há nela.</summary>
/// <param name="Itens">Itens da raiz, na ordem de exibição; grupo sem documentos não entra.</param>
public sealed record ListaDeDocumentosModel(IReadOnlyList<ItemDaListaDeDocumentosModel> Itens);

/// <summary>Um documento ou um grupo na lista, com os documentos abaixo dele.</summary>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre; nula quando não informada.</param>
/// <param name="EhGrupo">Se o item só reúne outros documentos: não tem arquivo, endereço, ícone nem badge.</param>
/// <param name="Url">Endereço do download; nulo no grupo.</param>
/// <param name="Icone">Classe <c>bi-*</c> pelo tipo do arquivo; nulo no grupo.</param>
/// <param name="Tipo">Etiqueta curta da extensão ("PDF", "DOCX"); nula no grupo.</param>
/// <param name="Detalhe">Texto pronto com tamanho e data ("120 KB · 12/10/2026"); nulo no grupo.</param>
/// <param name="Badge">A visibilidade do documento; nulo no grupo.</param>
/// <param name="Filhos">Documentos abaixo do item; vazio em quem não tem.</param>
public sealed record ItemDaListaDeDocumentosModel(
	string Titulo,
	string? Descricao,
	bool EhGrupo,
	string? Url,
	string? Icone,
	string? Tipo,
	string? Detalhe,
	BadgeModel? Badge,
	IReadOnlyList<ItemDaListaDeDocumentosModel> Filhos);
