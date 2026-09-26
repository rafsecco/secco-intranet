using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client;
using Secco.SecureGate.Client.Administration;
using Xunit;

namespace Secco.Intranet.Tests.Smoke;

/// <summary>
/// Fumaça contra um SecureGate <b>de verdade</b>: o adaptador e os handlers da gestão de acesso
/// falando com a API real, pelo client gerado e por client credentials — o que os testes com
/// dublê não conseguem provar. Só roda com o SecureGate no ar e as variáveis de ambiente
/// definidas; sem elas, cada teste aparece como pulado. Roteiro em
/// <c>docs/roteiro-fumaca-securegate.md</c>.
/// </summary>
public sealed class FumacaFactAttribute : FactAttribute
{
	/// <summary>Variável de ambiente com a URL base do SecureGate (ex.: <c>http://localhost:4101</c>).</summary>
	public const string VariavelDaUrl = "SECCO_SMOKE_SECUREGATE_URL";

	/// <summary>Inicializa o atributo, pulando o teste se a URL não estiver definida.</summary>
	public FumacaFactAttribute()
	{
		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariavelDaUrl)))
		{
			Skip = $"Fumaça contra o SecureGate real: defina {VariavelDaUrl} (ver docs/roteiro-fumaca-securegate.md).";
		}
	}
}

/// <summary>Conexão real com o SecureGate, montada pela mesma composição que a Intranet usa.</summary>
public sealed class FumacaFixture : IAsyncLifetime
{
	private readonly List<string> _perfisCriados = [];
	private readonly List<Guid> _usuariosCriados = [];
	private ServiceProvider? _provider;

	/// <summary>Tenant contra o qual a fumaça roda.</summary>
	public Guid Tenant { get; private set; }

	/// <summary>Sufixo único desta execução, para não colidir com dados de execuções anteriores.</summary>
	public string Sufixo { get; } = Guid.NewGuid().ToString("N")[..8];

	/// <summary>O client gerado, autenticado por client credentials.</summary>
	public ISecureGateClient Client { get; private set; } = null!;

	/// <summary>O adaptador sob teste.</summary>
	public SecureGateGestaoDeAcesso Gestao { get; private set; } = null!;

	/// <inheritdoc />
	public Task InitializeAsync()
	{
		var url = Environment.GetEnvironmentVariable(FumacaFactAttribute.VariavelDaUrl);

		if (string.IsNullOrWhiteSpace(url))
		{
			return Task.CompletedTask;
		}

		Tenant = Guid.Parse(Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_TENANT") ?? "018f0000-0000-7000-8000-000000000001");

		var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Secco:SecureGate:BaseUrl"] = url,
			["Secco:SecureGate:ClientId"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_ID") ?? "secco-dev-console",
			["Secco:SecureGate:ClientSecret"] = Environment.GetEnvironmentVariable("SECCO_SMOKE_SECUREGATE_CLIENT_SECRET")
				?? "secco-dev-console-secret-32-chars-min!",
		}).Build();

		_provider = new ServiceCollection()
			.AddSingleton<IConfiguration>(configuracao)
			.AddLogging()
			.AddSecureGateAdminClient()
			.BuildServiceProvider();

		Client = _provider.GetRequiredService<ISecureGateClient>();
		Gestao = new SecureGateGestaoDeAcesso(Client, new TenantFixo(Tenant), NullLogger<SecureGateGestaoDeAcesso>.Instance);

		return Task.CompletedTask;
	}

	/// <summary>Cria um perfil de nome único e o registra para limpeza.</summary>
	public async Task<string> CriarPerfilAsync(string prefixo)
	{
		var nome = $"{prefixo}-{Sufixo}";
		(await Gestao.CriarPerfilAsync(nome)).IsSuccess.Should().BeTrue($"criar o perfil {nome} para o cenário");
		_perfisCriados.Add(nome);

		return nome;
	}

	/// <summary>Cria um usuário de nome único e o registra para limpeza.</summary>
	public async Task<Guid> CriarUsuarioAsync(string apelido)
	{
		var usuario = await Client.CreateUserAsync(Tenant, new CreateUserRequest { Email = $"{apelido}-{Sufixo}@fumaca.secco.local" });
		_usuariosCriados.Add(usuario.Id);

		return usuario.Id;
	}

	/// <summary>Registra um perfil criado pelo próprio teste para ser removido no fim.</summary>
	public void Registrar(string perfil) => _perfisCriados.Add(perfil);

	/// <inheritdoc />
	public async Task DisposeAsync()
	{
		if (_provider is null)
		{
			return;
		}

		// Limpeza best-effort: o ambiente de DEV é descartável, e uma sobra não invalida a fumaça.
		foreach (var usuario in _usuariosCriados)
		{
			foreach (var perfil in _perfisCriados)
			{
				await Tentar(() => Client.RemoveUserRoleAsync(Tenant, usuario, perfil, CancellationToken.None));
			}

			await Tentar(() => Client.DeactivateUserAsync(Tenant, usuario, CancellationToken.None));
		}

		foreach (var perfil in _perfisCriados)
		{
			await Tentar(() => Client.DeleteRoleAsync(Tenant, perfil, CancellationToken.None));
		}

		await _provider.DisposeAsync();
	}

	private static async Task Tentar(Func<Task> acao)
	{
		try
		{
			await acao();
		}
		catch (ApiException)
		{
			// Já removido, recusado ou inexistente: não importa na limpeza.
		}
	}

	private sealed class TenantFixo(Guid tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => true;
	}
}

public class SecureGateGestaoDeAcessoFumacaTests(FumacaFixture f) : IClassFixture<FumacaFixture>
{
	[FumacaFact]
	public async Task Perfis_CriarListarObterExcluir_ContraOContratoReal()
	{
		var nome = await f.CriarPerfilAsync("fumaca-perfil");

		var lista = await f.Gestao.ListarPerfisAsync();
		lista.IsSuccess.Should().BeTrue();
		lista.Value.Should().Contain(p => p.Nome == nome && p.Tipo == TipoDePerfil.Comum && !p.Reservado);

		var detalhe = await f.Gestao.ObterPerfilAsync(nome);
		detalhe.IsSuccess.Should().BeTrue();
		detalhe.Value.Reservado.Should().BeFalse();
		detalhe.Value.TotalDeMembros.Should().Be(0);

		(await f.Gestao.CriarPerfilAsync(nome)).Error.Should().Be(IntranetErrors.Acesso.PerfilJaExiste,
			"a plataforma responde 409 a um nome repetido");

		(await f.Gestao.ExcluirPerfilAsync(nome)).IsSuccess.Should().BeTrue();
		(await f.Gestao.ObterPerfilAsync(nome)).Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado,
			"depois de excluído, o GetRole responde 404");
	}

	[FumacaFact]
	public async Task Perfil_ComMembros_NaoExclui()
	{
		var perfil = await f.CriarPerfilAsync("fumaca-membros");
		var usuario = await f.CriarUsuarioAsync("membro");

		(await f.Gestao.AtribuirPerfilAsync(usuario, perfil)).IsSuccess.Should().BeTrue();

		(await f.Gestao.ExcluirPerfilAsync(perfil)).Error.Should().Be(IntranetErrors.Acesso.PerfilComMembros,
			"a plataforma recusa perfil com membros com 409");
	}

	[FumacaFact]
	public async Task Usuario_AtribuirRetirar_RefleteNasLeiturasDeUsuarioEDeMembros()
	{
		var perfil = await f.CriarPerfilAsync("fumaca-atrib");
		var usuario = await f.CriarUsuarioAsync("atrib");

		(await f.Gestao.AtribuirPerfilAsync(usuario, perfil)).IsSuccess.Should().BeTrue();
		(await f.Gestao.AtribuirPerfilAsync(usuario, perfil)).IsSuccess.Should().BeTrue("atribuir é idempotente");

		var detalhe = await f.Gestao.ObterUsuarioAsync(usuario);
		detalhe.IsSuccess.Should().BeTrue();
		detalhe.Value.Perfis.Should().Contain(perfil);
		detalhe.Value.Situacao.Should().Be(SituacaoDoUsuario.Ativo);

		var membros = await f.Gestao.ListarMembrosAsync(perfil, 1);
		membros.IsSuccess.Should().BeTrue();
		membros.Value.Total.Should().Be(1);
		membros.Value.Itens.Should().ContainSingle(m => m.UsuarioId == usuario && m.Situacao == SituacaoDoUsuario.Ativo);

		var usuarios = await f.Gestao.ListarUsuariosAsync();
		usuarios.Value.Should().Contain(u => u.Id == usuario && u.Perfis.Contains(perfil));

		(await f.Gestao.RetirarPerfilAsync(usuario, perfil)).IsSuccess.Should().BeTrue();
		(await f.Gestao.RetirarPerfilAsync(usuario, perfil)).IsSuccess.Should().BeTrue("retirar é idempotente");
		(await f.Gestao.ListarMembrosAsync(perfil, 1)).Value.Total.Should().Be(0);
	}

	[FumacaFact]
	public async Task Usuario_DesativarReativarEncerrarSessoes()
	{
		var usuario = await f.CriarUsuarioAsync("situacao");

		(await f.Gestao.DesativarUsuarioAsync(usuario)).IsSuccess.Should().BeTrue();
		(await f.Gestao.ObterUsuarioAsync(usuario)).Value.Situacao.Should().Be(SituacaoDoUsuario.Desativado);

		(await f.Gestao.ReativarUsuarioAsync(usuario)).IsSuccess.Should().BeTrue();
		(await f.Gestao.ObterUsuarioAsync(usuario)).Value.Situacao.Should().Be(SituacaoDoUsuario.Ativo);

		(await f.Gestao.EncerrarSessoesAsync(usuario)).IsSuccess.Should().BeTrue();
	}

	[FumacaFact]
	public async Task UsuarioInexistente_Devolve404ComoNaoEncontrado()
	{
		var resultado = await f.Gestao.ObterUsuarioAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
		(await f.Gestao.AtribuirPerfilAsync(Guid.NewGuid(), "qualquer")).IsFailure.Should().BeTrue();
	}

	[FumacaFact]
	public async Task PerfilReservado_AComoAPlataformaMarca_ERecusadoPeloHandler()
	{
		var operador = await f.Gestao.ObterPerfilAsync("installation-operator");

		// O perfil reservado pode nem existir no tenant de DEV (404) — nesse caso o handler recusa
		// pela lista local, antes de perguntar. Se existir, o IsReserved da plataforma tem de ser true.
		if (operador.IsSuccess)
		{
			operador.Value.Reservado.Should().BeTrue("a plataforma marca installation-operator como reservado");
		}

		var usuario = await f.CriarUsuarioAsync("reservado");
		var resultado = await new AtribuirPerfilHandler(f.Gestao, new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(usuario, "installation-operator"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		(await f.Gestao.ObterUsuarioAsync(usuario)).Value.Perfis.Should().NotContain("installation-operator");
	}

	[FumacaFact]
	public async Task RegraDoUltimoIntranetAdmin_ComPaginacaoEMembrosReais()
	{
		// Perfil isolado com o nome que a regra observa não dá para usar (intranet-admin é do tenant
		// todo); o cenário roda no perfil real, mas só mexe em usuários criados aqui.
		var existente = await f.Gestao.ObterPerfilAsync(ClassificacaoDePerfil.IntranetAdmin);

		if (existente.IsFailure)
		{
			(await f.Gestao.CriarPerfilAsync(ClassificacaoDePerfil.IntranetAdmin)).IsSuccess.Should().BeTrue();
			f.Registrar(ClassificacaoDePerfil.IntranetAdmin);
		}

		var trilha = new TrilhaDeAcessoFalsa();
		var primeiro = await f.CriarUsuarioAsync("admin-a");
		var segundo = await f.CriarUsuarioAsync("admin-b");

		(await f.Gestao.AtribuirPerfilAsync(primeiro, ClassificacaoDePerfil.IntranetAdmin)).IsSuccess.Should().BeTrue();
		(await f.Gestao.AtribuirPerfilAsync(segundo, ClassificacaoDePerfil.IntranetAdmin)).IsSuccess.Should().BeTrue();

		var retirar = new RetirarPerfilHandler(f.Gestao, trilha, new AtorDeAcessoFalso(Guid.NewGuid()));

		// Com dois ativos, retirar um é permitido...
		(await retirar.HandleAsync(new RetirarPerfilCommand(primeiro, ClassificacaoDePerfil.IntranetAdmin)))
			.IsSuccess.Should().BeTrue();

		var restam = await f.Gestao.ListarMembrosAsync(ClassificacaoDePerfil.IntranetAdmin, 1);
		restam.IsSuccess.Should().BeTrue();
		restam.Value.Itens.Should().Contain(m => m.UsuarioId == segundo);

		// ...mas o último ativo não sai. Só dá para afirmar a recusa se o tenant de DEV não tem
		// outro intranet-admin ativo além do segundo — o que é o caso de um ambiente limpo.
		var outrosAtivos = restam.Value.Itens.Count(m => m.UsuarioId != segundo && m.Situacao == SituacaoDoUsuario.Ativo);

		if (outrosAtivos == 0)
		{
			(await retirar.HandleAsync(new RetirarPerfilCommand(segundo, ClassificacaoDePerfil.IntranetAdmin)))
				.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin);

			// A recusa é da Intranet: nada foi tocado na plataforma.
			(await f.Gestao.ListarMembrosAsync(ClassificacaoDePerfil.IntranetAdmin, 1))
				.Value.Itens.Should().Contain(m => m.UsuarioId == segundo);

			// E desativar o último também é recusado.
			(await new DesativarUsuarioHandler(f.Gestao, trilha, new AtorDeAcessoFalso(Guid.NewGuid())).HandleAsync(segundo))
				.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin);
		}

		// Limpeza: a regra é da Intranet (handler); o adaptador retira sem ela.
		(await f.Gestao.RetirarPerfilAsync(segundo, ClassificacaoDePerfil.IntranetAdmin)).IsSuccess.Should().BeTrue();
	}

	[FumacaFact]
	public async Task SecureGateInacessivel_ViraIndisponivel_SemExcecao()
	{
		var provider = new ServiceCollection()
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Secco:SecureGate:BaseUrl"] = "http://127.0.0.1:1",
				["Secco:SecureGate:ClientId"] = "x",
				["Secco:SecureGate:ClientSecret"] = "y",
			}).Build())
			.AddLogging()
			.AddSecureGateAdminClient()
			.BuildServiceProvider();

		var gestao = new SecureGateGestaoDeAcesso(
			provider.GetRequiredService<ISecureGateClient>(),
			new TenantFixoDaFumaca(f.Tenant),
			NullLogger<SecureGateGestaoDeAcesso>.Instance);

		var leitura = await gestao.ListarPerfisAsync();
		var escrita = await gestao.CriarPerfilAsync("qualquer");

		leitura.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		escrita.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[FumacaFact]
	public async Task Diretorio_ListaOsAtivos_ENaoOsDesativados_ContraOSecureGateReal()
	{
		var ativo = await f.CriarUsuarioAsync("dir-ativo");
		var desativado = await f.CriarUsuarioAsync("dir-desativado");
		(await f.Gestao.DesativarUsuarioAsync(desativado)).IsSuccess.Should().BeTrue();

		var fonte = new UsuariosParaDiretorioDoSecureGate(
			f.Gestao, new MemoryCache(new MemoryCacheOptions()), new TenantFixoDaFumaca(f.Tenant));

		var resultado = await fonte.ListarAtivosAsync();

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Should().Contain(usuario => usuario.Id == ativo && usuario.Email.Contains("dir-ativo"));
		resultado.Value.Should().NotContain(usuario => usuario.Id == desativado,
			"o diretório só mostra quem está ativo no SecureGate de verdade");
	}

	private sealed class TenantFixoDaFumaca(Guid tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => true;
	}
}
