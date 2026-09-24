using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Seeding;

namespace Secco.Intranet.Infrastructure.Seeding;

/// <summary>
/// Perfis de colaborador de amostra para navegar em desenvolvimento (ADR-0019). Idempotente:
/// grava só os usuários fictícios que ainda não têm perfil. Roda depois do seeder de setores
/// (a lotação aponta para o setor pelo slug).
/// </summary>
/// <param name="catalog">Catálogo de tenants.</param>
/// <param name="databaseOptions">Engine dos bancos de tenant.</param>
internal sealed class DiretorioDesenvolvimentoSeeder(
	ITenantCatalog catalog,
	IntranetDatabaseOptions databaseOptions) : IDevelopmentDataSeeder
{
	/// <summary>Depois dos setores (Order 0) e antes das publicações (Order 10).</summary>
	public int Order => 5;

	/// <summary>Aplica o seed em cada tenant do catálogo.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var options = IntranetDatabaseProviderConfigurator.CreateOptions(
				databaseOptions.Provider, tenant.ConnectionString);

			await using var context = new IntranetDbContext(options);

			var existentes = await context.PerfisColaboradores
				.Select(perfil => perfil.UsuarioId)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			var setores = await context.Setores
				.ToDictionaryAsync(setor => setor.Slug, setor => setor.Id, StringComparer.OrdinalIgnoreCase, cancellationToken)
				.ConfigureAwait(false);

			var novos = PessoasDeDesenvolvimento.Todas.Where(pessoa => !existentes.Contains(pessoa.Id)).ToList();

			if (novos.Count == 0)
			{
				continue;
			}

			foreach (var pessoa in novos)
			{
				var perfil = new PerfilColaborador(pessoa.Id);
				perfil.EditarContato(pessoa.Nome, pessoa.Ramal, null);
				perfil.EditarDadosFuncionais(
					pessoa.Cargo,
					setores.TryGetValue(pessoa.SetorSlug, out var setorId) ? setorId : null,
					pessoa.GestorId);

				context.PerfisColaboradores.Add(perfil);
			}

			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
