using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Seeding;

namespace Secco.Intranet.Infrastructure.Seeding;

/// <summary>
/// Publicações de amostra para o mural não nascer vazio em desenvolvimento (ADR-0019).
/// Idempotente: só insere títulos que ainda não existem.
/// </summary>
/// <param name="catalog">Catálogo de tenants.</param>
/// <param name="databaseOptions">Engine dos bancos de tenant.</param>
internal sealed class PublicacoesDesenvolvimentoSeeder(
	ITenantCatalog catalog,
	IntranetDatabaseOptions databaseOptions) : IDevelopmentDataSeeder
{
	private static readonly (string Slug, string Titulo, string Corpo, TipoPublicacao Tipo, Visibilidade Visibilidade, PrioridadePublicacao Prioridade, int DiasAtras)[] Amostra =
	[
		("recursos-humanos", "Recesso de fim de ano: como registrar as férias",
			"O período de recesso vai de **23 de dezembro a 2 de janeiro**.\n\nQuem for emendar férias precisa registrar a solicitação até o dia 30 deste mês.",
			TipoPublicacao.Aviso, Visibilidade.Empresa, PrioridadePublicacao.Importante, 1),
		("infraestrutura", "Manutenção programada da rede no sábado",
			"A rede interna ficará indisponível das **8h às 12h** do próximo sábado para troca do link principal.\n\nSistemas acessíveis pela internet seguem no ar.",
			TipoPublicacao.Aviso, Visibilidade.Empresa, PrioridadePublicacao.Urgente, 2),
		("diretoria", "Encontro trimestral de resultados",
			"Apresentação dos números do trimestre e das prioridades do próximo, no auditório do 4º andar, com transmissão para quem estiver remoto.",
			TipoPublicacao.Evento, Visibilidade.Empresa, PrioridadePublicacao.Normal, 4),
		("financeiro", "Nova política de reembolso entra em vigor",
			"Despesas passam a ser lançadas pelo próprio portal, com prazo de cinco dias úteis para aprovação do gestor direto.\n\n- Transporte e alimentação seguem as regras anteriores\n- Recibo digitalizado é obrigatório",
			TipoPublicacao.Noticia, Visibilidade.Empresa, PrioridadePublicacao.Normal, 6),
		("infraestrutura", "Procedimento interno de troca de credenciais",
			"Documento restrito ao setor: passo a passo da rotação trimestral.",
			TipoPublicacao.Aviso, Visibilidade.Setor, PrioridadePublicacao.Normal, 3),
	];

	/// <summary>Roda depois do seeder de setores: as publicações dependem deles.</summary>
	public int Order => 10;

	/// <summary>Aplica o seed de publicações de amostra em cada tenant do catálogo.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		var agora = DateTimeOffset.UtcNow;

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			var options = IntranetDatabaseProviderConfigurator.CreateOptions(
				databaseOptions.Provider, tenant.ConnectionString);

			await using var context = new Contexts.IntranetDbContext(options);

			var titulosExistentes = await context.Publicacoes
				.Select(publicacao => publicacao.Titulo)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			var setores = await context.Setores
				.ToDictionaryAsync(setor => setor.Slug, setor => setor.Id, cancellationToken)
				.ConfigureAwait(false);

			var novas = new List<Publicacao>();

			foreach (var amostra in Amostra)
			{
				if (titulosExistentes.Contains(amostra.Titulo, StringComparer.Ordinal)
					|| !setores.TryGetValue(amostra.Slug, out var setorId))
				{
					continue;
				}

				novas.Add(new Publicacao(
					setorId, amostra.Titulo, amostra.Corpo, amostra.Tipo, amostra.Visibilidade,
					amostra.Prioridade, agora.AddDays(-amostra.DiasAtras), null, "seed"));
			}

			if (novas.Count == 0)
			{
				continue;
			}

			context.Publicacoes.AddRange(novas);
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
