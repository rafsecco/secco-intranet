using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.SecureGate.Client;
using Secco.SecureGate.Client.Administration;
using Xunit;

namespace Secco.Intranet.Tests.Smoke;

/// <summary>
/// O adaptador de tenants contra um SecureGate <b>de verdade</b>: contrato do client gerado,
/// códigos de status e o formato de provisionamento. Só roda com <c>SECCO_SMOKE_SECUREGATE_URL</c>;
/// sem ela, aparece como pulado. Deixa um tenant desativado por execução — a plataforma não exclui
/// tenant (ver <c>docs/roteiro-fumaca-securegate.md</c>).
/// </summary>
public class SecureGateGestaoDeTenantsFumacaTests
{
	private static SecureGateGestaoDeTenants Gestao()
	{
		var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Secco:SecureGate:BaseUrl"] = Environment.GetEnvironmentVariable(FumacaFactAttribute.VariavelDaUrl),
			["Secco:SecureGate:ClientId"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_ID") ?? "secco-dev-console",
			["Secco:SecureGate:ClientSecret"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_SECRET")
				?? "secco-dev-console-secret-32-chars-min!",
		}).Build();

		var provider = new ServiceCollection()
			.AddSingleton<IConfiguration>(configuracao)
			.AddLogging()
			.AddSecureGateAdminClient()
			.BuildServiceProvider();

		return new SecureGateGestaoDeTenants(provider.GetRequiredService<ISecureGateClient>(), NullLogger<SecureGateGestaoDeTenants>.Instance);
	}

	/// <summary>Cria um tenant, provisiona o banco do LogStream, confere o status e desativa no fim.</summary>
	[FumacaFact]
	public async Task CicloDeVida_CriarProvisionarStatusDesativar_ContraOContratoReal()
	{
		var gestao = Gestao();
		var slug = $"fumaca-{Guid.NewGuid():N}"[..20];

		var criado = await gestao.CriarTenantAsync($"Fumaça {slug}", slug);
		criado.IsSuccess.Should().BeTrue(criado.IsFailure ? criado.Error.Description : null);

		try
		{
			(await gestao.CriarTenantAsync("Repetido", slug)).Error.Code.Should().Be("Intranet.Tenants.SlugJaExiste");

			var listados = await gestao.ListarTenantsAsync();
			listados.Value.Should().Contain(t => t.Id == criado.Value.Id);

			var provisionado = await gestao.ProvisionarAsync(criado.Value.Id, "logstream");
			provisionado.IsSuccess.Should().BeTrue(provisionado.IsFailure ? provisionado.Error.Description : null);
			(provisionado.Value.Aplicado || provisionado.Value.Script is { Length: > 0 }).Should().BeTrue(
				"ou a plataforma aplicou, ou devolveu o script para o DBA");

			var detalhe = await gestao.ObterTenantAsync(criado.Value.Id);
			detalhe.Value.Produtos.Should().Contain("logstream", "em modo script a connection string entra no catálogo antes do banco existir");

			var status = await gestao.ObterStatusDosBancosAsync(criado.Value.Id);
			status.Value.Should().Contain(s => s.Produto == "logstream");
		}
		finally
		{
			(await gestao.DesativarAsync(criado.Value.Id)).IsSuccess.Should().BeTrue();
		}

		(await gestao.ObterTenantAsync(criado.Value.Id)).Value.Ativo.Should().BeFalse();
	}

	/// <summary>Um tenant que não existe vira o erro de não encontrado.</summary>
	[FumacaFact]
	public async Task TenantInexistente_NaoEncontrado()
	{
		(await Gestao().ObterTenantAsync(Guid.NewGuid())).Error.Code.Should().Be("Intranet.Tenants.NaoEncontrado");
	}

	/// <summary>A plataforma recusa desativar o tenant de instalação (409).</summary>
	[FumacaFact]
	public async Task DesativarOTenantDeInstalacao_Recusado()
	{
		var resultado = await Gestao().DesativarAsync(TenantsProtegidosDoCatalogo.TenantDeInstalacao);

		resultado.IsFailure.Should().BeTrue("a plataforma protege o tenant de instalação (409)");
	}
}
