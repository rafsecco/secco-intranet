using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Extensibility;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>Tela de cadastro de salas (só intranet-admin) de ponta a ponta: formulário real até o handler.</summary>
public class SalasCadastroTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private HttpClient CriarAdmin() => CriarCliente("intranet-admin");

	private static string NomeUnico() => $"Sala {Guid.NewGuid():N}"[..20];

	private static FormUrlEncodedContent FormDaSala(string token, string nome, bool ativa = true, string? capacidade = "8") =>
		AuxiliaresDeHttp.Form(
			("__RequestVerificationToken", token),
			("Nome", nome),
			("Localizacao", "Andar 2"),
			("Capacidade", capacidade ?? string.Empty),
			("Recursos", "TV e quadro"),
			("EmailNaAgenda", string.Empty),
			("Ordem", "1"),
			("Ativa", ativa ? "true" : "false"));

	[Fact]
	public async Task Gerenciar_SemSerAdmin_403()
	{
		var id = Guid.NewGuid();

		foreach (var roles in new[] { Array.Empty<string>(), ["financeiro-admin"], ["financeiro-user"] })
		{
			var client = CriarCliente(roles);

			foreach (var url in new[] { "/salas/gerenciar", "/salas/gerenciar/nova", $"/salas/gerenciar/{id}/editar" })
			{
				(await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET {url} exige intranet-admin");
			}

			var corpo = AuxiliaresDeHttp.Form(("Nome", "Sala Pirata"));

			(await client.PostAsync("/salas/gerenciar/nova", corpo)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
			(await client.PostAsync($"/salas/gerenciar/{id}/excluir", corpo)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		}
	}

	[Fact]
	public async Task Nova_Valida_ApareceNaLista()
	{
		var client = CriarAdmin();
		var nome = NomeUnico();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/salas/gerenciar/nova");

		var resposta = await client.PostAsync("/salas/gerenciar/nova", FormDaSala(token, nome));
		var html = AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync());

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/salas/gerenciar");
		html.Should().Contain(nome).And.Contain("Andar 2");
	}

	[Fact]
	public async Task Nova_NomeRepetido_MostraErro()
	{
		var client = CriarAdmin();
		var nome = NomeUnico();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/salas/gerenciar/nova");
		(await client.PostAsync("/salas/gerenciar/nova", FormDaSala(token, nome))).EnsureSuccessStatusCode();

		var segunda = await client.PostAsync("/salas/gerenciar/nova", FormDaSala(token, nome.ToUpperInvariant()));
		var html = AuxiliaresDeHttp.Decodificar(await segunda.Content.ReadAsStringAsync());

		html.Should().Contain("Já existe uma sala com esse nome.");
	}

	[Fact]
	public async Task Editar_Desativa_MostraInativa()
	{
		var client = CriarAdmin();
		var nome = NomeUnico();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/salas/gerenciar/nova");
		(await client.PostAsync("/salas/gerenciar/nova", FormDaSala(token, nome))).EnsureSuccessStatusCode();
		var id = await IdDaSalaAsync(nome);

		var resposta = await client.PostAsync($"/salas/gerenciar/{id}/editar", FormDaSala(token, nome, ativa: false));
		var html = AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync());

		html.Should().Contain(nome);
		html.Should().Contain("Inativa");
		(await IdDaSalaAsync(nome)).Should().Be(id);
		await using var escopo = NovoEscopo();
		var sala = await escopo.ServiceProvider.GetRequiredService<ObterSalaHandler>().HandleAsync(id);
		sala.Value.Ativa.Should().BeFalse();
	}

	[Fact]
	public async Task Excluir_ComReservaFutura_MostraOrientacao()
	{
		var client = CriarAdmin();
		var nome = NomeUnico();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/salas/gerenciar/nova");
		(await client.PostAsync("/salas/gerenciar/nova", FormDaSala(token, nome))).EnsureSuccessStatusCode();
		var id = await IdDaSalaAsync(nome);

		await using (var escopo = NovoEscopo())
		{
			var inicio = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(30).AddHours(12), TimeSpan.Zero);
			var reserva = new Reserva(
				id, inicio, inicio.AddHours(1), "Alinhamento", null, Guid.NewGuid(), "Maria Teste", "maria.teste@exemplo.test", [], false);
			await escopo.ServiceProvider.GetRequiredService<IReservaRepository>().AddAsync(reserva);
		}

		var resposta = await client.PostAsync($"/salas/gerenciar/{id}/excluir", AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));
		var html = AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync());

		html.Should().Contain("Esta sala tem reservas futuras. Desative-a em vez de excluir.");
		html.Should().Contain(nome, "a sala continua na lista");
	}

	[Fact]
	public async Task Excluir_SemReserva_RemoveDaLista()
	{
		var client = CriarAdmin();
		var nome = NomeUnico();
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/salas/gerenciar/nova");
		(await client.PostAsync("/salas/gerenciar/nova", FormDaSala(token, nome))).EnsureSuccessStatusCode();
		var id = await IdDaSalaAsync(nome);

		var resposta = await client.PostAsync($"/salas/gerenciar/{id}/excluir", AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));
		var html = AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync());

		html.Should().NotContain(nome);
	}

	[Fact]
	public async Task ItemExtra_ComChaveSalas_EhIgnorado()
	{
		var html = await CriarAdmin().GetStringAsync("/salas/gerenciar");

		html.Should().NotContain("Extra que tenta ser Salas");
		html.Should().NotContain("href=\"/extra-que-tenta-ser-salas\"");
	}

	private AsyncServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	private async Task<Guid> IdDaSalaAsync(string nome)
	{
		await using var escopo = NovoEscopo();
		var salas = await escopo.ServiceProvider.GetRequiredService<ListarSalasHandler>().HandleAsync(somenteAtivas: false);

		return salas.Single(sala => sala.Nome == nome).Id;
	}

	/// <summary>Item de exemplo que tenta usar a chave reservada ao item fixo Salas.</summary>
	public sealed class ItemDeMenuComChaveSalas : IItemDeMenuPrincipal
	{
		public string Chave => "salas";

		public string Rotulo => "Extra que tenta ser Salas";

		public string? Icone => null;

		public string Url => "/extra-que-tenta-ser-salas";

		public int Ordem => 0;

		public Task<bool> VisivelParaAsync(System.Security.Claims.ClaimsPrincipal usuario, CancellationToken cancellationToken) =>
			Task.FromResult(true);
	}
}
