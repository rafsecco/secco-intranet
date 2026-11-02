namespace Secco.Intranet.Application.Bibliotecas;

/// <summary>
/// Segundos segmentos de URL que uma biblioteca não pode usar como slug: ela mora em
/// <c>/bibliotecas/{slug}</c> e as rotas fixas sob <c>/bibliotecas</c> vencem a dela. A
/// implementação mora no Web, que é quem enxerga as rotas registradas.
/// </summary>
public interface ISlugsReservadosDeBiblioteca
{
	/// <summary>Se o slug é reservado, sem diferenciar caixa e ignorando espaços nas pontas.</summary>
	/// <param name="slug">Slug candidato.</param>
	bool Contem(string? slug);
}
