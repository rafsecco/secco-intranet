using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class EscritaDeTenantsHandlersTests
{
	private static readonly Guid Instalacao = Guid.NewGuid();
	private static readonly Guid PropriaIntranet = Guid.NewGuid();
	private static readonly Guid OutraIntranet = Guid.NewGuid();

	private readonly GestaoDeTenantsFalsa _gestao = new();
	private readonly TenantsAdministradosFalso _cadastro = new();
	private readonly TrilhaDeAcessoFalsa _trilha = new();
	private readonly AtorDeAcessoFalso _ator = new(Guid.NewGuid());
	private readonly TenantsProtegidosFixos _protegidos = new(Instalacao, PropriaIntranet, OutraIntranet);

	private CriarTenantHandler Criar() => new(_gestao, _cadastro, _trilha, _ator);

	private AdotarTenantHandler Adotar() => new(_gestao, _cadastro, _protegidos, _trilha, _ator);

	private LigarRecursoHandler Ligar() => new(_cadastro, _gestao, _trilha);

	private AlterarSituacaoDoTenantHandler Situacao() => new(_cadastro, _gestao, _trilha);

	// ---- Criar

	[Fact]
	public async Task Criar_CriaNaPlataformaERegistraLocal_EAudita()
	{
		var resultado = await Criar().HandleAsync(new CriarTenantCommand(" Sistema de compras ", "Ana", "Compras", "compras"));

		resultado.IsSuccess.Should().BeTrue();
		_gestao.Chamadas.Should().Equal("criar:compras");
		var registro = _cadastro.Registros.Should().ContainSingle().Subject;
		registro.TenantId.Should().Be(resultado.Value);
		registro.Sistema.Should().Be("Sistema de compras");
		registro.Origem.Should().Be(OrigemDoTenant.Criado);
		registro.RegistradoPor.Should().Be("admin@exemplo.com");
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantCriar);
	}

	[Fact]
	public async Task Criar_EntradaInvalida_NaoChamaAPlataforma()
	{
		var resultado = await Criar().HandleAsync(new CriarTenantCommand("S", "Ana", "Compras", "Compras X"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.SlugInvalido);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_SlugDuplicado_DevolveOErroDaPlataforma_SemRegistrar()
	{
		_gestao.ComTenant(Guid.NewGuid(), "compras");

		var resultado = await Criar().HandleAsync(new CriarTenantCommand("S", "Ana", "Compras", "compras"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.SlugJaExiste);
		_cadastro.Registros.Should().BeEmpty();
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_PlataformaOkECadastroLocalFalha_OrientaAdotar_EAuditaACriacao()
	{
		_cadastro.FalharAoAdicionar = true;

		var resultado = await Criar().HandleAsync(new CriarTenantCommand("S", "Ana", "Compras", "compras"));

		resultado.Error.Code.Should().Be("Intranet.Tenants.RegistroLocalFalhou");
		resultado.Error.Description.Should().Contain("compras").And.Contain("Adotar");
		_trilha.Registros.Should().ContainSingle("o tenant existe na plataforma: a criação aconteceu e precisa constar")
			.Which.Verbo.Should().Be(VerbosDeAuditoria.TenantCriar);
	}

	// ---- Adotar

	[Theory]
	[MemberData(nameof(Protegidos))]
	public async Task Adotar_TenantProtegido_Recusa_SemChamarAPlataforma(Guid protegido)
	{
		var resultado = await Adotar().HandleAsync(new AdotarTenantCommand(protegido, "S", "Ana"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoAdotavel);
		_gestao.Chamadas.Should().BeEmpty();
		_cadastro.Registros.Should().BeEmpty();
	}

	public static TheoryData<Guid> Protegidos => new() { Instalacao, PropriaIntranet, OutraIntranet };

	[Fact]
	public async Task Adotar_JaAdministrado_Recusa()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "erp");
		_cadastro.Com(id);

		var resultado = await Adotar().HandleAsync(new AdotarTenantCommand(id, "S", "Ana"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.JaAdministrado);
	}

	[Fact]
	public async Task Adotar_GuidQueNaoExisteNaPlataforma_NaoEncontrado()
	{
		var resultado = await Adotar().HandleAsync(new AdotarTenantCommand(Guid.NewGuid(), "S", "Ana"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoEncontrado);
		_cadastro.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Adotar_Livre_RegistraComoAdotado_EAudita()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "erp");

		var resultado = await Adotar().HandleAsync(new AdotarTenantCommand(id, "ERP", "Bia"));

		resultado.IsSuccess.Should().BeTrue();
		_cadastro.Registros.Should().ContainSingle().Which.Origem.Should().Be(OrigemDoTenant.Adotado);
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAdotar);
	}

	// ---- Ligar recurso

	[Fact]
	public async Task Ligar_TenantForaDoCadastro_NaoEncontrado()
	{
		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(Guid.NewGuid(), "logstream"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoEncontrado);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("compras")]
	[InlineData("intranet")]
	public async Task Ligar_RecursoForaDaLista_Recusa(string? recurso)
	{
		var id = Guid.NewGuid();
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, recurso));

		resultado.Error.Should().Be(IntranetErrors.Tenants.RecursoInvalido);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Ligar_SecureGate_SoMarcaOCadastro()
	{
		var id = Guid.NewGuid();
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "securegate"));

		resultado.Value.Should().Be(new ProvisionamentoDto(true, null));
		_cadastro.Registros[0].SecureGateHabilitado.Should().BeTrue();
		_cadastro.Salvamentos.Should().Be(1);
		_gestao.Chamadas.Should().BeEmpty("o SecureGate não tem banco por tenant");
	}

	[Fact]
	public async Task Ligar_SecureGateJaLigado_RecursoJaLigado()
	{
		var id = Guid.NewGuid();
		_cadastro.Com(id, secureGate: true);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "securegate"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.RecursoJaLigado);
	}

	[Fact]
	public async Task Ligar_LogStream_ProvisionaComOProdutoDaLista_EAudita()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras");
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "LogStream"));

		resultado.Value.Aplicado.Should().BeTrue();
		_gestao.Chamadas.Should().Contain($"provisionar:{id}:logstream");
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantRecursoLigar);
	}

	[Fact]
	public async Task Ligar_DuasVezes_NaoReprovisiona()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras", true, "logstream");
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "logstream"));

		resultado.Error.Should().Be(IntranetErrors.Tenants.RecursoJaLigado);
		_gestao.Chamadas.Should().NotContain(c => c.StartsWith("provisionar", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Ligar_ModoScript_DevolveOScript_ETrilhaNuncaOContem()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras");
		_gestao.ModoScript = true;
		_cadastro.Com(id);

		var resultado = await Ligar().HandleAsync(new LigarRecursoCommand(id, "notificationhub"));

		resultado.Value.Should().Be(new ProvisionamentoDto(false, GestaoDeTenantsFalsa.Script));
		_trilha.Registros.Select(r => r.Verbo).Should().Equal(
			VerbosDeAuditoria.TenantRecursoLigar, VerbosDeAuditoria.TenantRecursoScriptGerado);
		_trilha.Registros.Should().OnlyContain(r => r.Metadata == null || !r.Metadata.Contains("SENHA-SECRETA-DE-TESTE"),
			"o script tem a senha db_owner e nunca vai para a trilha");
	}

	// ---- Ativar / desativar

	[Fact]
	public async Task Desativar_ConfirmacaoErrada_NaoChamaAPlataforma()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras");
		_cadastro.Com(id);

		var resultado = await Situacao().DesativarAsync(id, "Compras");

		resultado.Error.Should().Be(IntranetErrors.Tenants.ConfirmacaoNaoConfere);
		_gestao.Chamadas.Should().NotContain($"desativar:{id}");
	}

	[Fact]
	public async Task Desativar_ConfirmacaoCerta_DesativaEAudita()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras");
		_cadastro.Com(id);

		var resultado = await Situacao().DesativarAsync(id, " compras ");

		resultado.IsSuccess.Should().BeTrue();
		_gestao.Tenants[0].Ativo.Should().BeFalse();
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantDesativar);
	}

	[Fact]
	public async Task Ativar_ForaDoCadastro_NaoEncontrado()
	{
		var resultado = await Situacao().AtivarAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Tenants.NaoEncontrado);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Ativar_Cadastrado_AtivaEAudita()
	{
		var id = Guid.NewGuid();
		_gestao.ComTenant(id, "compras", ativo: false);
		_cadastro.Com(id);

		var resultado = await Situacao().AtivarAsync(id);

		resultado.IsSuccess.Should().BeTrue();
		_gestao.Tenants[0].Ativo.Should().BeTrue();
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAtivar);
	}
}
