using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Application.Tenants.Credenciais;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Regras das credenciais do tenant: validação antes da plataforma, tipo da credencial lida e trilha sem secret.</summary>
public class CredenciaisDoTenantHandlerTests
{
	private static readonly Guid Tenant = Guid.NewGuid();
	private const string ClientIdDeLogin = "cli_aaaaaaaaaaaaaaaa";
	private const string ClientIdDeMaquina = "cli_bbbbbbbbbbbbbbbb";

	private sealed record Cenario(
		CredenciaisDoTenantHandler Handler,
		LeituraDeCredenciaisHandler Leitura,
		GestaoDeCredenciaisDoTenantFalsa Credenciais,
		GestaoDeAcessoDoTenantFalsa Acesso,
		TrilhaDeAcessoFalsa Trilha);

	private static Cenario Criar(bool secureGate = true, bool permitirHttp = false, params string[] produtos)
	{
		var plataforma = new GestaoDeTenantsFalsa().ComTenant(Tenant, "compras", true, produtos.Length == 0 ? ["logstream"] : produtos);
		var cadastro = new TenantsAdministradosFalso().Com(Tenant, secureGate: secureGate);
		var acesso = new GestaoDeAcessoDoTenantFalsa().ComPerfil("compras-leitor").ComPerfil("installation-operator");
		var credenciais = new GestaoDeCredenciaisDoTenantFalsa()
			.ComCredencial(new CredencialDto(
				ClientIdDeLogin, "Portal", TipoDeCredencial.Login, ["logstream"], [], ["https://portal.exemplo.com/cb"], [], null))
			.ComCredencial(new CredencialDto(
				ClientIdDeMaquina, "Robo", TipoDeCredencial.Maquina, ["notificationhub"], [], [], [], null));
		var leitura = new LeituraDeCredenciaisHandler(credenciais, new ObterTenantAdministradoHandler(cadastro, plataforma), acesso);
		var trilha = new TrilhaDeAcessoFalsa();
		var handler = new CredenciaisDoTenantHandler(
			credenciais, leitura, new CredenciaisOptions { PermitirEnderecoHttp = permitirHttp }, trilha);

		return new Cenario(handler, leitura, credenciais, acesso, trilha);
	}

	private static FormularioDaCredencial Maquina(
		string? nome = "Integracao", string[]? escopos = null, string[]? perfis = null, string? redirects = null) =>
		new(nome, escopos ?? ["logstream"], perfis ?? [], redirects, null);

	private static FormularioDaCredencial Login(string? redirects = "https://app.exemplo.com/cb", string[]? perfis = null) =>
		new("Painel", ["logstream"], perfis ?? [], redirects, null);

	private static void SemEscrita(Cenario c) =>
		c.Credenciais.Chamadas.Should().NotContain(
			x => x.StartsWith("Emitir", StringComparison.Ordinal) || x.StartsWith("Alterar", StringComparison.Ordinal)
				|| x.StartsWith("Rotacionar", StringComparison.Ordinal) || x.StartsWith("Revogar", StringComparison.Ordinal));

	private static async Task Recusa(
		Cenario c, TipoDeCredencial tipo, FormularioDaCredencial formulario, Error esperado)
	{
		var resultado = await c.Handler.EmitirAsync(Tenant, tipo, formulario);

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Code.Should().Be(esperado.Code);
		SemEscrita(c);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Emitir_Maquina_ComLogstream_GravaETrilhaSemSecret()
	{
		var c = Criar();

		var resultado = await c.Handler.EmitirAsync(Tenant, TipoDeCredencial.Maquina, Maquina());

		resultado.IsSuccess.Should().BeTrue();
		c.Credenciais.DadosEmitidos!.Escopos.Should().Equal("logstream");
		c.Credenciais.DadosEmitidos.Tipo.Should().Be(TipoDeCredencial.Maquina);
		var registro = c.Trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantCredencialEmitir);
		registro.Metadata.Should().Contain(resultado.Value.Credencial.ClientId).And.NotContain("secret-de-teste");
	}

	[Fact]
	public async Task Emitir_EscopoDeRecursoDesligado_Recusa() =>
		await Recusa(Criar(), TipoDeCredencial.Maquina, Maquina(escopos: ["notificationhub"]), IntranetErrors.Credenciais.EscopoNaoLigado("NotificationHub"));

	[Fact]
	public async Task Emitir_EscopoForaDaLista_Recusa() =>
		await Recusa(Criar(), TipoDeCredencial.Maquina, Maquina(escopos: ["securegate:admin"]), IntranetErrors.Credenciais.EscopoNaoLigado("securegate:admin"));

	[Fact]
	public async Task Emitir_MaquinaSemEscopo_Recusa() =>
		await Recusa(Criar(), TipoDeCredencial.Maquina, Maquina(escopos: []), IntranetErrors.Credenciais.SemEscopo);

	[Fact]
	public async Task Emitir_Login_ComSecureGateDesligado_Recusa() =>
		await Recusa(Criar(secureGate: false), TipoDeCredencial.Login, Login(), IntranetErrors.Credenciais.LoginSemSecureGate);

	[Fact]
	public async Task Emitir_Login_SemEndereco_Recusa() =>
		await Recusa(Criar(), TipoDeCredencial.Login, Login(redirects: "  \n "), IntranetErrors.Credenciais.LoginSemEndereco);

	[Fact]
	public async Task Emitir_Login_ComPerfis_Recusa() =>
		await Recusa(Criar(), TipoDeCredencial.Login, Login(perfis: ["compras-leitor"]), IntranetErrors.Credenciais.LoginComPerfis);

	[Fact]
	public async Task Emitir_Login_HttpForaDeDevelopment_Recusa() =>
		await Recusa(Criar(permitirHttp: false), TipoDeCredencial.Login, Login("http://localhost/cb"), IntranetErrors.Credenciais.EnderecoRecusado);

	[Fact]
	public async Task Emitir_Login_HttpEmDevelopment_Aceita()
	{
		var c = Criar(permitirHttp: true);

		(await c.Handler.EmitirAsync(Tenant, TipoDeCredencial.Login, Login("http://localhost/cb"))).IsSuccess.Should().BeTrue();
		c.Credenciais.DadosEmitidos!.RedirectUris.Should().Equal("http://localhost/cb");
	}

	[Fact]
	public async Task Emitir_Login_OnzeEnderecos_Recusa()
	{
		var enderecos = string.Join('\n', Enumerable.Range(1, 11).Select(i => $"https://app.exemplo.com/cb{i}"));

		await Recusa(Criar(), TipoDeCredencial.Login, Login(enderecos), IntranetErrors.Credenciais.LimiteExcedido);
	}

	[Fact]
	public async Task Emitir_Maquina_ComEndereco_Recusa() =>
		await Recusa(Criar(), TipoDeCredencial.Maquina, Maquina(redirects: "https://app.exemplo.com/cb"), IntranetErrors.Credenciais.MaquinaComEndereco);

	[Fact]
	public async Task Emitir_PerfilReservado_Recusa() =>
		await Recusa(Criar(), TipoDeCredencial.Maquina, Maquina(perfis: ["installation-operator"]), IntranetErrors.Credenciais.PerfilRecusado);

	[Fact]
	public async Task Emitir_PerfilInexistente_Recusa() =>
		await Recusa(Criar(), TipoDeCredencial.Maquina, Maquina(perfis: ["nao-existe"]), IntranetErrors.Credenciais.PerfilRecusado);

	[Fact]
	public async Task Emitir_PerfilDoTenant_Aceita()
	{
		var c = Criar();

		(await c.Handler.EmitirAsync(Tenant, TipoDeCredencial.Maquina, Maquina(perfis: ["compras-leitor"]))).IsSuccess.Should().BeTrue();
		c.Credenciais.DadosEmitidos!.Perfis.Should().Equal("compras-leitor");
	}

	[Fact]
	public async Task Emitir_PerfilComSecureGateDesligado_Recusa()
	{
		var c = Criar(secureGate: false);

		await Recusa(c, TipoDeCredencial.Maquina, Maquina(perfis: ["compras-leitor"]), IntranetErrors.Credenciais.PerfisSemSecureGate);
		c.Acesso.Chamadas.Should().BeEmpty();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Emitir_NomeVazioOuSoEspacos_Recusa(string? nome) =>
		await Recusa(Criar(), TipoDeCredencial.Maquina, Maquina(nome: nome), IntranetErrors.Credenciais.NomeInvalido);

	[Fact]
	public async Task Alterar_UsaOTipoDaCredencialLida()
	{
		var c = Criar();

		var resultado = await c.Handler.AlterarAsync(Tenant, ClientIdDeLogin, Login(perfis: ["compras-leitor"]));

		resultado.Error.Should().Be(IntranetErrors.Credenciais.LoginComPerfis);
		SemEscrita(c);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Alterar_RecursoDesligadoDepois_Recusa()
	{
		var c = Criar();

		var resultado = await c.Handler.AlterarAsync(Tenant, ClientIdDeMaquina, Maquina(escopos: ["notificationhub"]));

		resultado.Error.Code.Should().Be("Intranet.Credenciais.EscopoNaoLigado");
		resultado.Error.Description.Should().Contain("NotificationHub");
		SemEscrita(c);
	}

	[Fact]
	public async Task Alterar_Valida_Grava_ERegistraNaTrilha()
	{
		var c = Criar();

		var resultado = await c.Handler.AlterarAsync(Tenant, ClientIdDeMaquina, Maquina(nome: " Robo 2 ", perfis: ["compras-leitor"]));

		resultado.IsSuccess.Should().BeTrue();
		c.Credenciais.DadosAlterados!.Should().Match<DadosDaCredencial>(d => d.Nome == "Robo 2" && d.Tipo == TipoDeCredencial.Maquina);
		var registro = c.Trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantCredencialAlterar);
		registro.Metadata.Should().Contain(ClientIdDeMaquina);
	}

	[Fact]
	public async Task Alterar_ClientIdForaDoFormato_NaoEncontrada_SemChamarAPorta()
	{
		var c = Criar();

		(await c.Handler.AlterarAsync(Tenant, "../x", Maquina())).Error.Should().Be(IntranetErrors.Credenciais.NaoEncontrada);
		c.Credenciais.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Revogar_ConfirmacaoDiferente_Recusa()
	{
		var c = Criar();

		(await c.Handler.RevogarAsync(Tenant, ClientIdDeLogin, "portal")).Error.Should().Be(IntranetErrors.Credenciais.ConfirmacaoNaoConfere);
		SemEscrita(c);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Revogar_ComONomeExato_RevogaERegistra()
	{
		var c = Criar();

		(await c.Handler.RevogarAsync(Tenant, ClientIdDeLogin, "Portal")).IsSuccess.Should().BeTrue();

		c.Credenciais.Chamadas.Should().Contain($"Revogar:{Tenant}:{ClientIdDeLogin}");
		var registro = c.Trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantCredencialRevogar);
		registro.Metadata.Should().Contain(ClientIdDeLogin).And.Contain("Portal");
	}

	[Fact]
	public async Task Rotacionar_TrilhaSemSecret()
	{
		var c = Criar();

		var resultado = await c.Handler.RotacionarAsync(Tenant, ClientIdDeMaquina);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Valor.Should().StartWith("secret-de-teste");
		var registro = c.Trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantCredencialRotacionar);
		registro.Metadata.Should().Contain(ClientIdDeMaquina).And.NotContain("secret-de-teste");
	}

	[Fact]
	public async Task Escrita_FalhaNaPlataforma_NaoRegistraNaTrilha()
	{
		var c = Criar();
		c.Credenciais.FalharCom = IntranetErrors.Credenciais.NomeEmUso;

		var resultado = await c.Handler.EmitirAsync(Tenant, TipoDeCredencial.Maquina, Maquina());

		resultado.Error.Should().Be(IntranetErrors.Credenciais.NomeEmUso);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Obter_ClientIdForaDoFormato_NaoEncontrada_SemChamarAPorta()
	{
		var c = Criar();

		(await c.Leitura.ObterAsync(Tenant, "cli_curto")).Error.Should().Be(IntranetErrors.Credenciais.NaoEncontrada);
		(await c.Leitura.ObterAsync(Tenant, null)).Error.Should().Be(IntranetErrors.Credenciais.NaoEncontrada);
		c.Credenciais.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Opcoes_SecureGateDesligado_SemPerfisESemChamarAcesso()
	{
		var c = Criar(secureGate: false);

		var opcoes = await c.Leitura.OpcoesAsync(Tenant);

		opcoes.IsSuccess.Should().BeTrue();
		opcoes.Value.SecureGateLigado.Should().BeFalse();
		opcoes.Value.PerfisDisponiveis.Should().BeEmpty();
		opcoes.Value.EscoposDisponiveis.Should().Equal("logstream");
		c.Acesso.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Opcoes_SecureGateLigado_ListaPerfisSemOsReservados()
	{
		var c = Criar();

		var opcoes = await c.Leitura.OpcoesAsync(Tenant);

		opcoes.Value.PerfisDisponiveis.Select(p => p.Nome).Should().Equal("compras-leitor");
	}
}
