using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Persistência de verdade, contra o banco do tenant: o teste do dublê de repositório não prova
/// que o mapeamento, o índice único e o rastreamento funcionam.
/// </summary>
public class PerfilColaboradorPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private IServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	[Fact]
	public async Task Adicionar_EBuscar_GravaDeVerdade()
	{
		var usuario = Guid.NewGuid();
		var perfil = new PerfilColaborador(usuario);
		perfil.EditarContato("Ana Ribeiro", "2100", "Sobre a Ana");

		using (var escopo = NovoEscopo())
		{
			var criado = await escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>().TentarAdicionarAsync(perfil);
			criado.Should().BeTrue();
		}

		// Escopo novo, DbContext novo: só sobrevive o que foi para o banco.
		using var leitura = NovoEscopo();
		var lido = await leitura.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>().GetByUsuarioIdAsync(usuario);

		lido.Should().NotBeNull();
		lido!.NomeExibicao.Should().Be("Ana Ribeiro");
		lido.Ramal.Should().Be("2100");
	}

	[Fact]
	public async Task SegundoPerfilDoMesmoUsuario_DevolveFalse_SemLancar()
	{
		var usuario = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			(await escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>()
				.TentarAdicionarAsync(new PerfilColaborador(usuario))).Should().BeTrue();
		}

		using var outro = NovoEscopo();
		var repetido = await outro.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>()
			.TentarAdicionarAsync(new PerfilColaborador(usuario));

		repetido.Should().BeFalse("o índice único barra o segundo, e a corrida não vira 500");
	}

	[Fact]
	public async Task Editar_Rastreado_GravaDeVerdade()
	{
		var usuario = Guid.NewGuid();
		var gestor = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			await escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>()
				.TentarAdicionarAsync(new PerfilColaborador(usuario));
		}

		using (var escopo = NovoEscopo())
		{
			var repositorio = escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>();
			var perfil = await repositorio.GetParaEdicaoAsync(usuario);
			perfil!.EditarDadosFuncionais("Analista", null, gestor);
			await repositorio.SaveChangesAsync();
		}

		using var leitura = NovoEscopo();
		var lido = await leitura.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>().GetByUsuarioIdAsync(usuario);

		lido!.Cargo.Should().Be("Analista");
		lido.GestorUsuarioId.Should().Be(gestor);
	}

	[Fact]
	public async Task ListarTodos_TrazOsPerfisDoTenant()
	{
		var usuario = Guid.NewGuid();

		using (var escopo = NovoEscopo())
		{
			await escopo.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>()
				.TentarAdicionarAsync(new PerfilColaborador(usuario));
		}

		using var leitura = NovoEscopo();
		var todos = await leitura.ServiceProvider.GetRequiredService<IPerfilColaboradorRepository>().ListarTodosAsync();

		todos.Should().Contain(perfil => perfil.UsuarioId == usuario);
	}
}
