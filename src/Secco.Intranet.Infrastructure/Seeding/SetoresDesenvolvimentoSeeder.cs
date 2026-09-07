using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain.Setores;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Seeding;

namespace Secco.Intranet.Infrastructure.Seeding;

/// <summary>
/// Setores de amostra para navegar na aplicação em desenvolvimento (ADR-0019). Idempotente:
/// insere apenas os slugs que ainda não existem no banco de cada tenant.
/// </summary>
/// <remarks>
/// É deliberadamente um seeder de <b>desenvolvimento</b>, e não de referência — inclusive
/// para o setor fixo Infraestrutura. Gravar um setor direto no banco pula o provisionamento
/// das Roles no SecureGate que o <c>CreateSetorHandler</c> faz (ADR-0001); em produção o
/// setor precisa nascer pelo fluxo de cadastro, com as Roles junto.
/// </remarks>
/// <param name="catalog">Catálogo de tenants.</param>
/// <param name="databaseOptions">Engine dos bancos de tenant.</param>
internal sealed class SetoresDesenvolvimentoSeeder(
	ITenantCatalog catalog,
	IntranetDatabaseOptions databaseOptions) : IDevelopmentDataSeeder
{
	private static readonly (string Nome, string Slug, bool Fixo)[] Amostra =
	[
		("Infraestrutura", "infraestrutura", true),
		("Recursos Humanos", "recursos-humanos", false),
		("Financeiro", "financeiro", false),
		("Diretoria", "diretoria", false),
	];

	/// <summary>Roda primeiro: as publicações de amostra dependem dos setores.</summary>
	public int Order => 0;

	/// <summary>Aplica o seed de setores de amostra em cada tenant do catálogo.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var options = IntranetDatabaseProviderConfigurator.CreateOptions(
				databaseOptions.Provider, tenant.ConnectionString);

			await using var context = new Contexts.IntranetDbContext(options);

			var slugsExistentes = await context.Setores
				.Select(setor => setor.Slug)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			var novos = Amostra
				.Where(amostra => !slugsExistentes.Contains(amostra.Slug, StringComparer.OrdinalIgnoreCase))
				.Select(amostra => new Setor(amostra.Nome, amostra.Slug, amostra.Fixo))
				.ToList();

			if (novos.Count == 0)
			{
				continue;
			}

			context.Setores.AddRange(novos);
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
