namespace Secco.Intranet.Application.Documentos;

/// <summary>Espécie de dono de um documento.</summary>
public enum TipoDeDono
{
	/// <summary>Setor.</summary>
	Setor = 1,

	/// <summary>Biblioteca de documentos.</summary>
	Biblioteca = 2,
}

/// <summary>
/// Referência de um dono de documento pelo slug, que é o que as rotas e as permissões conhecem.
/// O slug é normalizado (trim e minúsculas) em qualquer caminho de construção, então duas
/// referências ao mesmo dono são sempre iguais.
/// </summary>
public sealed record ReferenciaDeDono
{
	/// <summary>Cria a referência, normalizando o slug.</summary>
	/// <param name="tipo">Espécie do dono.</param>
	/// <param name="slug">Slug do dono.</param>
	public ReferenciaDeDono(TipoDeDono tipo, string slug)
	{
		Tipo = tipo;
		Slug = (slug ?? string.Empty).Trim().ToLowerInvariant();
	}

	/// <summary>Espécie do dono.</summary>
	public TipoDeDono Tipo { get; }

	/// <summary>Slug normalizado do dono.</summary>
	public string Slug { get; }

	/// <summary>Referência a um setor.</summary>
	/// <param name="slug">Slug do setor.</param>
	public static ReferenciaDeDono DoSetor(string slug) => new(TipoDeDono.Setor, slug);

	/// <summary>Referência a uma biblioteca.</summary>
	/// <param name="slug">Slug da biblioteca.</param>
	public static ReferenciaDeDono DaBiblioteca(string slug) => new(TipoDeDono.Biblioteca, slug);
}
