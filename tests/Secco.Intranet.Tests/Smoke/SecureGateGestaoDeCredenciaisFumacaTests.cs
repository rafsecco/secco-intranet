using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants.Credenciais;
using Secco.Intranet.Infrastructure.Access;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.SecureGate.Client;
using Secco.SecureGate.Client.Administration;
using Xunit;

namespace Secco.Intranet.Tests.Smoke;

/// <summary>
/// Credenciais de tenant administrado contra um SecureGate <b>de verdade</b>. Prova, além do contrato, que
/// o secret emitido pede token, que o secret antigo morre na rotação e que um tenant não enxerga o
/// client de outro. Deixa um tenant desativado por execução.
/// </summary>
public class SecureGateGestaoDeCredenciaisFumacaTests
{
	private static ServiceProvider Provider()
	{
		var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Secco:SecureGate:BaseUrl"] = Environment.GetEnvironmentVariable(FumacaFactAttribute.VariavelDaUrl),
			["Secco:SecureGate:ClientId"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_ID") ?? "secco-dev-console",
			["Secco:SecureGate:ClientSecret"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_SECRET")
				?? "secco-dev-console-secret-32-chars-min!",
		}).Build();

		return new ServiceCollection()
			.AddSingleton<IConfiguration>(configuracao)
			.AddLogging()
			.AddSecureGateAdminClient()
			.BuildServiceProvider();
	}

	private static async Task<HttpResponseMessage> PedirTokenAsync(HttpClient http, string clientId, string secret)
	{
		var baseUrl = Environment.GetEnvironmentVariable(FumacaFactAttribute.VariavelDaUrl)!.TrimEnd('/');
		using var pedido = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = secret,
			["scope"] = "logstream",
		});

		return await http.PostAsync($"{baseUrl}/connect/token", pedido);
	}

	private static string TenantIdDoJwt(string jwt)
	{
		var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
		payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
		using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

		return json.RootElement.GetProperty("tenant_id").GetString()!;
	}

	[FumacaFact]
	public async Task EmitirTokenRotacionarIsolarRevogar_ContraOContratoReal()
	{
		using var provider = Provider();
		using var http = new HttpClient();
		var client = provider.GetRequiredService<ISecureGateClient>();
		var tenants = new SecureGateGestaoDeTenants(client, NullLogger<SecureGateGestaoDeTenants>.Instance);
		var credenciais = new SecureGateGestaoDeCredenciaisDoTenant(client, NullLogger<SecureGateGestaoDeCredenciaisDoTenant>.Instance);

		var sufixo = Guid.NewGuid().ToString("N")[..10];
		var tenant = (await tenants.CriarTenantAsync($"Fumaça credenciais {sufixo}", $"fc-{sufixo}")).Value.Id;

		try
		{
			// 1. Máquina com logstream.
			var maquina = await credenciais.EmitirAsync(
				tenant,
				new DadosDaCredencial(TipoDeCredencial.Maquina, $"Máquina {sufixo}", ["logstream"], [], [], []));
			maquina.IsSuccess.Should().BeTrue(maquina.IsFailure ? maquina.Error.Description : null);
			var clientIdMaquina = maquina.Value.Credencial.ClientId;
			clientIdMaquina.Should().MatchRegex(@"^cli_[a-z2-7]{16}$");
			maquina.Value.Secret.Valor.Should().NotBeNullOrEmpty();

			// 2. O secret emitido pede token, e o JWT é do tenant.
			var token = await PedirTokenAsync(http, clientIdMaquina, maquina.Value.Secret.Valor);
			var corpo = await token.Content.ReadAsStringAsync();
			token.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, "o secret recém-emitido vale para o escopo logstream");
			using (var resposta = JsonDocument.Parse(corpo))
			{
				TenantIdDoJwt(resposta.RootElement.GetProperty("access_token").GetString()!).Should().Be(tenant.ToString());
			}

			// 3. Rotação derruba o secret antigo.
			var rotacionado = await credenciais.RotacionarSecretAsync(tenant, clientIdMaquina);
			rotacionado.IsSuccess.Should().BeTrue(rotacionado.IsFailure ? rotacionado.Error.Description : null);
			var antigo = await PedirTokenAsync(http, clientIdMaquina, maquina.Value.Secret.Valor);
			((int)antigo.StatusCode).Should().BeOneOf(400, 401);

			// 4. Outro tenant não enxerga o client.
			(await credenciais.ObterAsync(TenantsProtegidosDoCatalogo.TenantDeInstalacao, clientIdMaquina))
				.Error.Should().Be(IntranetErrors.Credenciais.NaoEncontrada);

			// 5. Login com endereço de retorno.
			var login = await credenciais.EmitirAsync(
				tenant,
				new DadosDaCredencial(
					TipoDeCredencial.Login,
					$"Login {sufixo}",
					["logstream"],
					[],
					["https://compras.exemplo.test/signin-oidc"],
					[]));
			login.IsSuccess.Should().BeTrue(login.IsFailure ? login.Error.Description : null);
			var clientIdLogin = login.Value.Credencial.ClientId;
			(await credenciais.ObterAsync(tenant, clientIdLogin)).Value.Tipo.Should().Be(TipoDeCredencial.Login);

			// 6. Revogar as duas.
			(await credenciais.RevogarAsync(tenant, clientIdMaquina)).IsSuccess.Should().BeTrue();
			(await credenciais.RevogarAsync(tenant, clientIdLogin)).IsSuccess.Should().BeTrue();
			(await credenciais.ObterAsync(tenant, clientIdMaquina)).Error.Should().Be(IntranetErrors.Credenciais.NaoEncontrada);
			(await credenciais.ObterAsync(tenant, clientIdLogin)).Error.Should().Be(IntranetErrors.Credenciais.NaoEncontrada);
		}
		finally
		{
			await tenants.DesativarAsync(tenant);
		}
	}
}
