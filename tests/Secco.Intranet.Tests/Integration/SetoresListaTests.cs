using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>Lista administrativa de setores (<c>/Setores</c>).</summary>
public class SetoresListaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Lista_MostraOIconeDeCadaSetor()
	{
		// A lista desenhava o mesmo ícone fixo para todos: trocar o ícone do setor não aparecia nela.
		var slug = $"icone-{Guid.NewGuid():N}"[..20];

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

			var criado = await escopo.ServiceProvider
				.GetRequiredService<CreateSetorHandler>()
				.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug, Icone: "bi-cash-coin"));

			criado.IsSuccess.Should().BeTrue();
		}

		var cliente = factory.CreateClient();
		cliente.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		cliente.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");

		var html = WebUtility.HtmlDecode(await cliente.GetStringAsync($"/Setores?nome={slug}"));

		var linha = Regex.Match(html, "<li class=\"sc-list__item[^\"]*\">(.*?)</li>", RegexOptions.Singleline);
		linha.Success.Should().BeTrue("a busca pelo slug acha o setor criado");
		linha.Groups[1].Value.Should().Contain($"Setor {slug}");
		linha.Groups[1].Value.Should().Contain("bi bi-cash-coin");
	}
}
