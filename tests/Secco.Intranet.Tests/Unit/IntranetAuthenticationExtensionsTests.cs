using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Secco.Intranet.Web.Authentication;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Teste unitário da composição de autenticação (ADR-0012): sem host real, só a coleção de
/// serviços resultante de <see cref="IntranetAuthenticationExtensions.AddIntranetAuthentication"/>.
/// </summary>
public class IntranetAuthenticationExtensionsTests
{
	private sealed class FakeHostEnvironment : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = "Development";
		public string ApplicationName { get; set; } = "Secco.Intranet.Web";
		public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
		public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
			new Microsoft.Extensions.FileProviders.NullFileProvider();
	}

	private static IConfiguration BuildConfiguration(bool withAuthority) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(withAuthority
				? new Dictionary<string, string?>
				{
					["Secco:SecureGate:Authority"] = "https://securegate.dev.local",
					["Secco:SecureGate:ClientId"] = "intranet-maquina",
					["Secco:SecureGate:ClientSecret"] = "segredo-maquina",
					["Intranet:Login:ClientId"] = "intranet-login",
					["Intranet:Login:ClientSecret"] = "segredo-login",
				}
				: [])
			.Build();

	[Fact]
	public async Task AddIntranetAuthentication_WithAuthorityConfigured_RegistersSchemesAndFallbackPolicy()
	{
		var configuration = BuildConfiguration(withAuthority: true);
		var services = new ServiceCollection();
		services.AddSingleton(configuration);
		services.AddLogging();

		services.AddIntranetAuthentication(configuration, new FakeHostEnvironment());

		await using var provider = services.BuildServiceProvider();

		var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
		var schemes = (await schemeProvider.GetAllSchemesAsync()).Select(scheme => scheme.Name).ToList();

		schemes.Should().Contain(CookieAuthenticationDefaults.AuthenticationScheme);
		schemes.Should().Contain(OpenIdConnectDefaults.AuthenticationScheme);

		provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy.Should().NotBeNull();
	}

	[Fact]
	public async Task AddIntranetAuthentication_UsaOClientDeLogin_NaoOClientDeMaquina()
	{
		var configuration = BuildConfiguration(withAuthority: true);
		var services = new ServiceCollection();
		services.AddSingleton(configuration);
		services.AddLogging();

		services.AddIntranetAuthentication(configuration, new FakeHostEnvironment());

		await using var provider = services.BuildServiceProvider();

		var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
			.Get(OpenIdConnectDefaults.AuthenticationScheme);

		// O SecureGate dá um único grant a cada client de plataforma (ADR-0037): o de login é
		// AuthorizationCode e o de Secco:SecureGate é ClientCredentials — não são o mesmo.
		oidc.ClientId.Should().Be("intranet-login");
		oidc.ClientSecret.Should().Be("segredo-login");
	}

	[Fact]
	public void AddIntranetAuthentication_WithoutAuthority_RegistersNothing()
	{
		var configuration = BuildConfiguration(withAuthority: false);

		IntranetAuthenticationExtensions.IsConfigured(configuration).Should().BeFalse();

		var services = new ServiceCollection();
		services.AddIntranetAuthentication(configuration, new FakeHostEnvironment());

		using var provider = services.BuildServiceProvider();

		provider.GetService<IAuthenticationSchemeProvider>().Should().BeNull();
	}
}
