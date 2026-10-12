using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Infrastructure.Tenants;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Adaptador da Área de tenants: recusa da plataforma com motivo conhecido vira mensagem própria,
/// nunca "SecureGate indisponível" — quem administra precisa saber o que corrigir.
/// </summary>
public class SecureGateGestaoDeTenantsTests
{
	private sealed class ClientFalso(Exception falha) : SecureGateClient(new HttpClient())
	{
		public override Task<TenantDatabaseProvisioningDto> ProvisionTenantDatabaseAsync(
			Guid id, string product, ProvisionTenantDatabaseRequest body, CancellationToken cancellationToken) =>
			Task.FromException<TenantDatabaseProvisioningDto>(falha);
	}

	private static ApiException Api(int status, string corpo = "") =>
		new("erro", status, corpo, new Dictionary<string, IEnumerable<string>>(), null!);

	private static SecureGateGestaoDeTenants Gestao(Exception falha) =>
		new(new ClientFalso(falha), NullLogger<SecureGateGestaoDeTenants>.Instance);

	[Fact]
	public async Task Provisionar_SemAlvoConfiguradoNaPlataforma_ViraErroProprio()
	{
		var corpo = "{\"title\":\"SecureGate.TenantDatabase.ProvisioningServerRequired\",\"status\":400}";

		var provisionado = await Gestao(Api(400, corpo)).ProvisionarAsync(Guid.NewGuid(), "logstream");

		provisionado.Error.Should().Be(IntranetErrors.Tenants.ProvisionamentoSemAlvo);
	}

	[Fact]
	public async Task Provisionar_400SemMotivoConhecido_ContinuaIndisponivel()
	{
		var provisionado = await Gestao(Api(400)).ProvisionarAsync(Guid.NewGuid(), "logstream");

		provisionado.Error.Should().Be(IntranetErrors.Tenants.Indisponivel);
	}
}
