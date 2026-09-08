using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Setores;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Editar precisa <b>gravar</b>. Contra fake isso não se prova: um repositório falso marca
/// "gravou" e o teste passa mesmo quando a entidade voltou desrastreada do EF e o
/// <c>SaveChanges</c> não teve nada a fazer — caso em que o caso de uso devolve sucesso e o
/// banco continua igual.
/// </summary>
public class EditarSetorPersistenciaTests(IntranetWebFactory factory)
	: IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<Guid> CriarAsync(string slug)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criado = await escopo.ServiceProvider
			.GetRequiredService<CreateSetorHandler>()
			.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));

		criado.IsSuccess.Should().BeTrue();

		return criado.Value.Id;
	}

	[Fact]
	public async Task Editar_GravaDeVerdade()
	{
		var slug = $"edit-{Guid.NewGuid():N}"[..20];
		var id = await CriarAsync(slug);

		using (var escopo = factory.Services.CreateScope())
		{
			escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

			var resultado = await escopo.ServiceProvider
				.GetRequiredService<EditarSetorHandler>()
				.HandleAsync(new EditarSetorCommand(id, "Nome Novo", "bi-hdd-rack", Ativo: false));

			resultado.IsSuccess.Should().BeTrue();
		}

		// Escopo novo, DbContext novo: só sobrevive o que foi para o banco.
		using var leitura = factory.Services.CreateScope();
		leitura.ServiceProvider.SetTenant(factory.TenantAlfa);

		var lido = await leitura.ServiceProvider
			.GetRequiredService<GetSetorByIdHandler>()
			.HandleAsync(id);

		lido.IsSuccess.Should().BeTrue();
		lido.Value.Nome.Should().Be("Nome Novo");
		lido.Value.Icone.Should().Be("bi-hdd-rack");
		lido.Value.Ativo.Should().BeFalse();
		lido.Value.Slug.Should().Be(slug, "o slug não é editável");
	}
}
