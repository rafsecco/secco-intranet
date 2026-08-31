namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Ação exibida no cabeçalho de uma página.</summary>
/// <param name="Texto">Rótulo do botão. Diz o que acontece ao acionar, no infinitivo do produto.</param>
/// <param name="Url">Destino.</param>
/// <param name="Icone">Classe do ícone (opcional).</param>
/// <param name="Primaria">Se recebe o destaque da cor primária.</param>
public sealed record PageActionModel(string Texto, string Url, string? Icone = null, bool Primaria = false);

/// <summary>Cabeçalho de página: título, apoio e ações.</summary>
/// <param name="Titulo">Título da página.</param>
/// <param name="Subtitulo">Linha de apoio (opcional).</param>
/// <param name="SetorSlug">Slug do setor da página, quando houver; define o matiz do cabeçalho.</param>
/// <param name="Acoes">Ações à direita.</param>
public sealed record PageHeaderModel(
	string Titulo,
	string? Subtitulo = null,
	string? SetorSlug = null,
	IReadOnlyList<PageActionModel>? Acoes = null);
