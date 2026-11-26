using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.Intranet.Web.Hosting;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// <c>Intranet:Formatos:Cultura</c> decide como as telas mostram e leem datas: padrão <c>pt-BR</c>,
/// outra cultura muda campos e textos, e cultura inexistente impede a subida.
/// </summary>
public class FormatosConfiguracaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static IServiceCollection Registrar(params (string Chave, string Valor)[] valores)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(valores.ToDictionary(valor => valor.Chave, valor => (string?)valor.Valor))
			.Build();

		return new ServiceCollection().AddIntranetFormatos(configuration);
	}

	[Fact]
	public void SemConfiguracao_UsaPtBr()
	{
		using var servicos = Registrar().BuildServiceProvider();

		servicos.GetRequiredService<FormatoDeDatas>().Cultura.Name.Should().Be("pt-BR");
	}

	[Fact]
	public void CulturaInexistente_ImpedeASubida()
	{
		var registrar = () => Registrar((FormatoDeDatas.ChaveDaCultura, "xx-NAO-EXISTE"));

		registrar.Should().Throw<InvalidOperationException>().WithMessage("*Intranet:Formatos:Cultura*");
	}

	[Fact]
	public async Task OutraCultura_MudaAGradeEOFormulario()
	{
		var sala = new Sala($"Sala {Guid.NewGuid():N}"[..20], null, 6, null, null, 1);
		await using (var escopo = factory.Services.CreateAsyncScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
			await escopo.ServiceProvider.GetRequiredService<ISalaRepository>().AddAsync(sala);
		}

		var client = factory
			.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
			{
				services.RemoveAll<FormatoDeDatas>();
				services.AddSingleton(new FormatoDeDatas("en-US"));
			}))
			.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, "intranet-admin");
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, Guid.NewGuid().ToString());

		var grade = AuxiliaresDeHttp.Decodificar(await client.GetStringAsync($"/salas?data=2030-01-03&sala={sala.Id}"));
		var formulario = AuxiliaresDeHttp.Decodificar(await client.GetStringAsync($"/salas/reservar?sala={sala.Id}&data=2030-01-03"));

		grade.Should().Contain("Thursday, 01/03/2030").And.Contain("placeholder=\"mm/dd/yyyy\"");
		formulario.Should().Contain("value=\"01/03/2030\"");
	}
}
