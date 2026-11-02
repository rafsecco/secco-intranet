using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Domain.Bibliotecas;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// <see cref="IBibliotecaRepository"/> sobre uma lista, para testes de unidade que só precisam
/// resolver bibliotecas pelo slug.
/// </summary>
public sealed class BibliotecaRepositorioFalso(params Biblioteca[] bibliotecas) : IBibliotecaRepository
{
	private readonly List<Biblioteca> _bibliotecas = [.. bibliotecas];

	public Task AddAsync(Biblioteca biblioteca, CancellationToken cancellationToken = default)
	{
		_bibliotecas.Add(biblioteca);

		return Task.CompletedTask;
	}

	public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task<Biblioteca?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		Task.FromResult(_bibliotecas.FirstOrDefault(item => item.Id == id));

	public Task<Biblioteca?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
		Task.FromResult(_bibliotecas.FirstOrDefault(
			item => string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase)));

	public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
		Task.FromResult(_bibliotecas.Any(item => string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase)));

	public Task<IReadOnlyList<Biblioteca>> ListarAsync(bool somenteAtivas, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<Biblioteca>>([.. _bibliotecas.Where(item => !somenteAtivas || item.Ativo)]);
}
