namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Intenção visual de um badge.</summary>
public enum BadgeVariante
{
	/// <summary>Cor derivada do slug do setor (ver <see cref="SetorHue"/>).</summary>
	Setor = 0,

	/// <summary>Cinza neutro.</summary>
	Neutro = 1,

	/// <summary>Estado positivo.</summary>
	Sucesso = 2,

	/// <summary>Estado que pede atenção.</summary>
	Aviso = 3,

	/// <summary>Estado de erro ou bloqueio.</summary>
	Perigo = 4,
}

/// <summary>Etiqueta curta de classificação.</summary>
/// <param name="Texto">Rótulo.</param>
/// <param name="Variante">Intenção visual.</param>
/// <param name="SetorSlug">Slug do setor, usado quando a variante é <see cref="BadgeVariante.Setor"/>.</param>
public sealed record BadgeModel(string Texto, BadgeVariante Variante = BadgeVariante.Setor, string? SetorSlug = null);
