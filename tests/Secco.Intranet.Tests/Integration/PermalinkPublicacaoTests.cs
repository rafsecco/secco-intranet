using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O permalink é o destino do aviso. Inexistente e fora do alcance devolvem a mesma resposta,
/// pelo motivo de sempre: distinguir revelaria a existência.
/// </summary>
public class PermalinkPublicacaoTests(IntranetWebFactory factory)
	: IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CriarCliente()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		return client;
	}

	private async Task<Guid> SemearAsync(DateTimeOffset publicadoEm)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var slug = $"perma-{Guid.NewGuid():N}"[..20];
		var criarSetor = escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>();
		var setor = await criarSetor.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
		setor.IsSuccess.Should().BeTrue();

		var repositorio = escopo.ServiceProvider.GetRequiredService<IPublicacaoRepository>();
		var publicacao = new Publicacao(
			setor.Value.Id, "Comunicado do permalink", "Corpo em **markdown**.", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal, publicadoEm, null, "teste");

		await repositorio.AddAsync(publicacao);

		return publicacao.Id;
	}

	[Fact]
	public async Task NoAr_AbreComOCorpoRenderizado()
	{
		var id = await SemearAsync(DateTimeOffset.UtcNow.AddMinutes(-5));

		var resposta = await CriarCliente().GetAsync($"/publicacoes/{id}");
		var html = await resposta.Content.ReadAsStringAsync();

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain("Comunicado do permalink");
		html.Should().Contain("<strong>markdown</strong>");
	}

	[Fact]
	public async Task Agendada_NaoAbre()
	{
		var id = await SemearAsync(DateTimeOffset.UtcNow.AddDays(1));

		var resposta = await CriarCliente().GetAsync($"/publicacoes/{id}");

		resposta.StatusCode.Should().Be(
			HttpStatusCode.NotFound, "o permalink aplica a mesma regra de relógio da listagem");
	}

	[Fact]
	public async Task Inexistente_RespondeIgualAoQueNaoPodeSerVisto()
	{
		var agendada = await SemearAsync(DateTimeOffset.UtcNow.AddDays(1));
		var client = CriarCliente();

		var semAcesso = await client.GetAsync($"/publicacoes/{agendada}");
		var inexistente = await client.GetAsync($"/publicacoes/{Guid.NewGuid()}");

		semAcesso.StatusCode.Should().Be(inexistente.StatusCode);
	}
}
