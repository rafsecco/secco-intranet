using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Tests.Support;

/// <summary>Slugs reservados fixos, para os testes de unidade que não sobem as rotas do Web.</summary>
/// <param name="slugs">Os slugs que contam como reservados.</param>
public sealed class SlugsReservadosFalsos(params string[] slugs) : ISlugsReservados
{
	private readonly HashSet<string> _slugs = new(slugs, StringComparer.OrdinalIgnoreCase);

	public bool Contem(string? slug) => slug is not null && _slugs.Contains(slug.Trim());
}
