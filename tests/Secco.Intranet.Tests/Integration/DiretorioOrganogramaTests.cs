using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

public class DiretorioOrganogramaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(UsuariosParaDiretorioFalso? usuarios, params string[] roles)
	{
		factory.UsuariosDoDiretorio = usuarios;
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private async Task DefinirGestorAsync(UsuariosParaDiretorioFalso usuarios, Guid pessoa, Guid gestor, string? nome = null)
	{
		factory.UsuariosDoDiretorio = usuarios;
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		(await escopo.ServiceProvider.GetRequiredService<EditarDadosFuncionaisHandler>()
			.HandleAsync(new EditarDadosFuncionaisCommand(pessoa, null, null, gestor))).IsSuccess.Should().BeTrue();

		if (nome is not null)
		{
			(await escopo.ServiceProvider.GetRequiredService<EditarContatoHandler>()
				.HandleAsync(new EditarContatoCommand(pessoa, nome, null, null))).IsSuccess.Should().BeTrue();
		}
	}

	[Theory]
	[InlineData("financeiro-admin")]
	[InlineData("inventario-admin")]
	[InlineData("financeiro-user")]
	public async Task SemNenhumDosTresPerfis_403(string role)
	{
		var resposta = await CriarCliente(new UsuariosParaDiretorioFalso(), role).GetAsync("/diretorio/organograma");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task SemPerfil_403_ETambemSemRoleNenhuma()
	{
		var resposta = await CriarCliente(new UsuariosParaDiretorioFalso()).GetAsync("/diretorio/organograma");

		resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ComAcesso_MostraAChefiaComAEquipeAninhada()
	{
		var chefe = Guid.NewGuid();
		var membro = Guid.NewGuid();
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var usuarios = new UsuariosParaDiretorioFalso().Com(chefe, $"chefe{sufixo}@x.com").Com(membro, $"membro{sufixo}@x.com");
		await DefinirGestorAsync(usuarios, membro, chefe);

		var html = Decodificar(await CriarCliente(usuarios, "diretorio-user").GetStringAsync("/diretorio/organograma"));

		html.Should().Contain($"chefe{sufixo}@x.com").And.Contain($"membro{sufixo}@x.com");
		html.IndexOf($"chefe{sufixo}@x.com", StringComparison.Ordinal)
			.Should().BeLessThan(html.IndexOf($"membro{sufixo}@x.com", StringComparison.Ordinal), "a chefia vem antes da equipe");
	}

	[Fact]
	public async Task GestorDesativado_AEquipeApareceComAMarca()
	{
		var chefe = Guid.NewGuid();
		var membro = Guid.NewGuid();
		var sufixo = Guid.NewGuid().ToString("N")[..6];
		var todos = new UsuariosParaDiretorioFalso().Com(chefe, $"chefe{sufixo}@x.com").Com(membro, $"membro{sufixo}@x.com");
		await DefinirGestorAsync(todos, membro, chefe);

		// O chefe é desativado: sai da lista de usuários ativos, mas o perfil do membro ainda aponta para ele.
		var soMembro = new UsuariosParaDiretorioFalso().Com(membro, $"membro{sufixo}@x.com");
		var outro = Guid.NewGuid();
		soMembro.Com(outro, $"outro{sufixo}@x.com");
		await DefinirGestorAsync(soMembro.Com(Guid.NewGuid(), $"terceiro{sufixo}@x.com"), outro, membro);

		var html = Decodificar(await CriarCliente(soMembro, "diretorio-user").GetStringAsync("/diretorio/organograma"));

		html.Should().Contain("gestor inativo", "o membro perdeu o gestor, mas a equipe dele não some");
		html.Should().Contain($"outro{sufixo}@x.com");
	}

	[Fact]
	public async Task SemSecureGate_ExplicaSemQuebrar()
	{
		var resposta = await CriarCliente(usuarios: null, "diretorio-user").GetAsync("/diretorio/organograma");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		Decodificar(await resposta.Content.ReadAsStringAsync()).Should().Contain("SecureGate não está configurado");
	}
}
