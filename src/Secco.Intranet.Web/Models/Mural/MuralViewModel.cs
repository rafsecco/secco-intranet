namespace Secco.Intranet.Web.Models.Mural;

/// <summary>Natureza de uma publicação do mural.</summary>
public enum TipoPublicacao
{
	/// <summary>Comunicado interno.</summary>
	Aviso = 0,

	/// <summary>Acontecimento com data marcada.</summary>
	Evento = 1,

	/// <summary>Notícia institucional.</summary>
	Noticia = 2,
}

/// <summary>
/// Uma publicação do mural. Um único recurso com discriminador de tipo: o formato do
/// conteúdo é o mesmo, e separar em entidades distintas duplicaria a tela de cadastro.
/// </summary>
/// <param name="Titulo">Título da publicação.</param>
/// <param name="Resumo">Texto de abertura.</param>
/// <param name="PublicadoEm">Data de publicação.</param>
/// <param name="Tipo">Natureza da publicação.</param>
/// <param name="SetorNome">Nome do setor que publicou.</param>
/// <param name="SetorSlug">Slug do setor, que define a cor do card.</param>
public sealed record PublicacaoViewModel(
	string Titulo,
	string Resumo,
	DateTimeOffset PublicadoEm,
	TipoPublicacao Tipo,
	string SetorNome,
	string SetorSlug);

/// <summary>Modelo da página do mural.</summary>
/// <param name="Publicacoes">Publicações já filtradas.</param>
/// <param name="Filtro">Tipo selecionado; <c>null</c> significa todos.</param>
/// <param name="Demonstracao">Se o conteúdo exibido é de demonstração.</param>
public sealed record MuralViewModel(
	IReadOnlyList<PublicacaoViewModel> Publicacoes,
	TipoPublicacao? Filtro,
	bool Demonstracao);
