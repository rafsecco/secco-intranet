namespace Secco.Intranet.Application.Setores;

/// <summary>
/// Primeiros segmentos de URL que um setor não pode usar como slug: o setor mora na raiz
/// (<c>/{slug}/…</c>) e as rotas fixas vencem a dele. A implementação mora no Web, que é quem
/// enxerga as rotas registradas — inclusive as de uma RCL referenciada.
/// </summary>
public interface ISlugsReservados
{
	/// <summary>Se o slug é reservado, sem diferenciar caixa e ignorando espaços nas pontas.</summary>
	/// <param name="slug">Slug candidato.</param>
	bool Contem(string? slug);
}
