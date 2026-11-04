using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Secco.Intranet.Tests.Integration.Exportacao;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Limite de exportação por pessoa (ADR-0058 da plataforma), com o limite baixado para 2 por minuto.
/// Cada teste sobe o próprio host, para o contador começar zerado.
/// </summary>
public class ExportacaoLimiteTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private WebApplicationFactory<Program> ComLimiteDeDois() =>
		factory.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Exportacao:PorPessoaPorMinuto", "2"));

	private HttpClient CriarCliente(WebApplicationFactory<Program> host, Guid usuario)
	{
		var client = host.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuario.ToString());

		return client;
	}

	private static async Task<HttpStatusCode> ExportarAsync(HttpClient client)
	{
		var token = await client.GetStringAsync(ExportacaoDeTesteController.Caminho);
		var resposta = await client.PostAsync(
			ExportacaoDeTesteController.Caminho,
			Form(("formato", "xlsx"), ("__RequestVerificationToken", token)));

		return resposta.StatusCode;
	}

	[Fact]
	public async Task AcimaDoLimite_429()
	{
		using var host = ComLimiteDeDois();
		var client = CriarCliente(host, Guid.NewGuid());

		(await ExportarAsync(client)).Should().Be(HttpStatusCode.OK);
		(await ExportarAsync(client)).Should().Be(HttpStatusCode.OK);
		(await ExportarAsync(client)).Should().Be(HttpStatusCode.TooManyRequests);
	}

	[Fact]
	public async Task Limite_ePorPessoa()
	{
		using var host = ComLimiteDeDois();
		var ana = CriarCliente(host, Guid.NewGuid());
		var bruno = CriarCliente(host, Guid.NewGuid());

		(await ExportarAsync(ana)).Should().Be(HttpStatusCode.OK);
		(await ExportarAsync(ana)).Should().Be(HttpStatusCode.OK);

		(await ExportarAsync(bruno)).Should().Be(HttpStatusCode.OK, "o limite de uma pessoa não bloqueia a outra");
	}
}
