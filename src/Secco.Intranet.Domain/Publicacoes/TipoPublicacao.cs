namespace Secco.Intranet.Domain.Publicacoes;

/// <summary>Natureza de uma publicação do mural. Na v1 é apenas rótulo de filtro.</summary>
public enum TipoPublicacao
{
	/// <summary>Comunicado interno.</summary>
	Aviso = 0,

	/// <summary>Acontecimento com data marcada.</summary>
	Evento = 1,

	/// <summary>Notícia institucional.</summary>
	Noticia = 2,
}
