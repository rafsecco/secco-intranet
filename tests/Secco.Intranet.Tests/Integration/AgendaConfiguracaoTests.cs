using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Application.Salas.Agenda;
using Secco.Intranet.Infrastructure.Salas;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A configuração da agenda externa é validada na subida do host, não no primeiro ciclo. Sobe um host
/// mínimo só com <c>AddAgendaExterna</c> (sem banco nem Docker), como a do chat; o caso "sem provedor"
/// usa a aplicação inteira, para provar o que a tela enxerga.
/// </summary>
public class AgendaConfiguracaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>
{
	private const string Segredo = "segredo-super-secreto-123";

	private sealed class CatalogoVazio : ITenantCatalog
	{
		public ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<TenantInfo?>(null);

		public ValueTask<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<IReadOnlyList<TenantInfo>>([]);
	}

	private static Dictionary<string, string?> Microsoft() => new()
	{
		["Intranet:Agenda:Provedor"] = "Microsoft",
		["Intranet:Agenda:IntervaloEmSegundos"] = "3600",
		["Intranet:Agenda:Microsoft:TenantId"] = Guid.NewGuid().ToString(),
		["Intranet:Agenda:Microsoft:ClientId"] = Guid.NewGuid().ToString(),
		["Intranet:Agenda:Microsoft:ClientSecret"] = Segredo,
		["Intranet:Agenda:Microsoft:CaixaOrganizadora"] = "reservas@exemplo.test",
	};

	private static IHost Host(Dictionary<string, string?> valores)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

		return new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddLogging();
				services.AddSingleton<ITenantCatalog, CatalogoVazio>();
				services.AddAgendaExterna(configuration);
			})
			.Build();
	}

	private static IEnumerable<string> Mensagens(Exception excecao)
	{
		for (var atual = excecao; atual is not null; atual = atual.InnerException)
		{
			yield return atual.Message;
		}
	}

	[Fact]
	public void SemProvedor_SobeSemWorker()
	{
		factory.Services.GetServices<IHostedService>().Should().NotContain(h => h is EspelhamentoWorker);
		factory.Services.GetRequiredService<IEspelhamentoLigado>().Ligado.Should().BeFalse();
	}

	[Fact]
	public async Task ProvedorVazio_NoHostMinimo_NaoRegistraNada()
	{
		using var host = Host(new() { ["Intranet:Agenda:Provedor"] = "  " });

		await host.StartAsync();

		host.Services.GetServices<IHostedService>().Should().NotContain(h => h is EspelhamentoWorker);
		host.Services.GetService<IAgendaExterna>().Should().BeNull();
		await host.StopAsync();
	}

	[Theory]
	[InlineData("Google")]
	[InlineData("xyz")]
	public async Task ProvedorDesconhecido_NaoSobe(string valor)
	{
		using var host = Host(new() { ["Intranet:Agenda:Provedor"] = valor });

		var subir = () => host.StartAsync();

		var excecao = (await subir.Should().ThrowAsync<OptionsValidationException>()).Which;
		Mensagens(excecao).Should().Contain(mensagem => mensagem.Contains("Intranet:Agenda:Provedor", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("Microsoft")]
	[InlineData(" microsoft ")]
	[InlineData("MICROSOFT")]
	public async Task Microsoft_Valido_LigaOEspelhamento(string provedor)
	{
		var valores = Microsoft();
		valores["Intranet:Agenda:Provedor"] = provedor;
		using var host = Host(valores);

		await host.StartAsync();

		host.Services.GetServices<IHostedService>().Should().ContainSingle(h => h is EspelhamentoWorker);
		host.Services.GetRequiredService<IEspelhamentoLigado>().Ligado.Should().BeTrue();
		host.Services.GetRequiredService<IAgendaExterna>().Should().NotBeNull();
		await host.StopAsync();
	}

	[Theory]
	[InlineData("Intranet:Agenda:Microsoft:TenantId", "nao-e-guid")]
	[InlineData("Intranet:Agenda:Microsoft:TenantId", "")]
	[InlineData("Intranet:Agenda:Microsoft:ClientId", "nao-e-guid")]
	[InlineData("Intranet:Agenda:Microsoft:ClientId", "00000000-0000-0000-0000-000000000000")]
	[InlineData("Intranet:Agenda:Microsoft:ClientSecret", "")]
	[InlineData("Intranet:Agenda:Microsoft:ClientSecret", "   ")]
	[InlineData("Intranet:Agenda:Microsoft:CaixaOrganizadora", "sem-arroba")]
	[InlineData("Intranet:Agenda:Microsoft:CaixaOrganizadora", "")]
	[InlineData("Intranet:Agenda:IntervaloEmSegundos", "4")]
	[InlineData("Intranet:Agenda:IntervaloEmSegundos", "0")]
	public async Task Microsoft_ChaveInvalida_NaoSobe_ECitaAChaveSemOSegredo(string chave, string valor)
	{
		var valores = Microsoft();
		valores[chave] = valor;
		using var host = Host(valores);

		var subir = () => host.StartAsync();

		var excecao = (await subir.Should().ThrowAsync<OptionsValidationException>()).Which;
		var mensagens = Mensagens(excecao).ToList();
		mensagens.Should().Contain(mensagem => mensagem.Contains(chave, StringComparison.Ordinal));
		mensagens.Should().NotContain(mensagem => mensagem.Contains(Segredo, StringComparison.Ordinal));
	}

	[Fact]
	public async Task Microsoft_IntervaloNoMinimo_Sobe()
	{
		var valores = Microsoft();
		valores["Intranet:Agenda:IntervaloEmSegundos"] = "5";
		using var host = Host(valores);

		await host.StartAsync();
		await host.StopAsync();
	}
}
