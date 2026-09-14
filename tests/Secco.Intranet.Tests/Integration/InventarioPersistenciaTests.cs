using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Domain.Inventario;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Editar/mudar status precisa <b>gravar</b>. Contra fake isso não se prova — ver
/// <c>EditarSetorPersistenciaTests</c>, cujo motivo de existir foi um bug real
/// (repositório desrastreado usado em caminho de escrita, sucesso devolvido sem gravar).
/// </summary>
public class InventarioPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<Guid> CriarAsync()
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CriarItemInventarioHandler>()
			.HandleAsync(new CriarItemInventarioCommand("Notebook Dell", null, "Equipamento", "PAT-001", null));

		criado.IsSuccess.Should().BeTrue();

		return criado.Value.Id;
	}

	[Fact]
	public async Task Editar_GravaDeVerdade()
	{
		var id = await CriarAsync();

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

			var resultado = await escopo.ServiceProvider
				.GetRequiredService<EditarItemInventarioHandler>()
				.HandleAsync(new EditarItemInventarioCommand(id, "Notebook Dell G15", "Nova descrição", "TI", "PAT-002", null));

			resultado.IsSuccess.Should().BeTrue();
		}

		// Escopo novo, DbContext novo: só sobrevive o que foi para o banco.
		using var leitura = factory.Services.CreateScope();
		leitura.ServiceProvider.SetTenant(factory.TenantAlfa);

		var lido = await leitura.ServiceProvider
			.GetRequiredService<GetItemInventarioByIdHandler>()
			.HandleAsync(id);

		lido.IsSuccess.Should().BeTrue();
		lido.Value.Nome.Should().Be("Notebook Dell G15");
		lido.Value.CodigoPatrimonio.Should().Be("PAT-002");
	}

	[Fact]
	public async Task MudarStatus_GravaDeVerdade()
	{
		var id = await CriarAsync();
		var usuarioId = Guid.NewGuid();

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

			var resultado = await escopo.ServiceProvider
				.GetRequiredService<MudarStatusItemInventarioHandler>()
				.HandleAsync(new MudarStatusItemInventarioCommand(id, AcaoDeStatus.Atribuir, usuarioId, "ana@exemplo.local"));

			resultado.IsSuccess.Should().BeTrue();
		}

		using var leitura = factory.Services.CreateScope();
		leitura.ServiceProvider.SetTenant(factory.TenantAlfa);

		var lido = await leitura.ServiceProvider
			.GetRequiredService<GetItemInventarioByIdHandler>()
			.HandleAsync(id);

		lido.Value.Status.Should().Be(StatusDoItem.EmUso);
		lido.Value.AtribuidoAUsuarioId.Should().Be(usuarioId);
		lido.Value.AtribuidoANome.Should().Be("ana@exemplo.local");
	}
}
