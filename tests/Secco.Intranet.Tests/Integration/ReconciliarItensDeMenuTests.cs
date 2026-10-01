using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Setores criados antes da árvore de itens de menu não têm nenhum item — a página deles abre
/// em 404. "Reconciliar itens de menu" (na lista de setores) é o que os traz de volta.
/// </summary>
public class ReconciliarItensDeMenuTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		return client;
	}

	/// <summary>Grava o setor direto no banco, como um setor de antes desta feature — sem árvore.</summary>
	private async Task<string> CriarSetorAntigoAsync()
	{
		var slug = $"ant-{Guid.NewGuid():N}"[..20];

		await using var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		contexto.Setores.Add(new Setor($"Setor {slug}", slug));
		await contexto.SaveChangesAsync();

		return slug;
	}

	[Fact]
	public async Task SetorAntigo_SemArvore_PassaAAbrirDepoisDeReconciliar()
	{
		var slug = await CriarSetorAntigoAsync();
		var client = CriarCliente();

		(await client.GetAsync($"/{slug}/documentos")).StatusCode.Should().Be(HttpStatusCode.NotFound,
			"sem raiz na árvore não há o que resolver");

		var html = await client.GetStringAsync("/Setores");
		var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
		token.Should().NotBeEmpty("a lista de setores traz o formulário de reconciliar");

		var resposta = await client.PostAsync("/Setores/ReconciliarItensDeMenu", new FormUrlEncodedContent(
			[new KeyValuePair<string, string>("__RequestVerificationToken", token)]));

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/Setores");

		var pagina = await client.GetAsync($"/{slug}/documentos");
		pagina.StatusCode.Should().Be(HttpStatusCode.OK, "setor antigo reconciliado ganha Documentos de volta");
	}
}
