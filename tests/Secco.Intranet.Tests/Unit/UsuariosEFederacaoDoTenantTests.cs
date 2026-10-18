using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Cadastro, perfis, convite, conta e federação de pessoas do tenant administrado.</summary>
public class UsuariosEFederacaoDoTenantTests
{
	private static readonly Guid Tenant = Guid.NewGuid();
	private static readonly Guid ComSenha = Guid.NewGuid();
	private static readonly Guid SemSenha = Guid.NewGuid();
	private static readonly Guid SoEntra = Guid.NewGuid();
	private static readonly Guid Diretorio = Guid.NewGuid();

	private static (UsuariosDoTenantHandler Usuarios, FederacaoDoTenantHandler Federacao, GestaoDeAcessoDoTenantFalsa Gestao, TrilhaDeAcessoFalsa Trilha) Criar(
		bool federacaoHabilitada = false)
	{
		var gestao = new GestaoDeAcessoDoTenantFalsa()
			.ComPerfil("compras-aprovador")
			.ComPerfil("compras-leitor")
			.ComPerfil("installation-operator", reservado: true)
			.ComUsuario(ComSenha, "ana@compras.com", SituacaoDoUsuario.Ativo, temSenha: true)
			.ComUsuario(SemSenha, "bia@compras.com", SituacaoDoUsuario.Ativo, temSenha: false, entraPorSenha: true)
			.ComUsuario(SoEntra, "caio@compras.com", SituacaoDoUsuario.Ativo, temSenha: false, entraPorSenha: false);
		gestao.Federacao = new FederacaoDto(federacaoHabilitada ? Diretorio : null, federacaoHabilitada, null);
		var trilha = new TrilhaDeAcessoFalsa();

		return (new UsuariosDoTenantHandler(gestao, trilha), new FederacaoDoTenantHandler(gestao, trilha), gestao, trilha);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("sem-arroba")]
	[InlineData("ana @compras.com")]
	public async Task Cadastrar_EmailInvalido(string? email)
	{
		var (usuarios, _, gestao, _) = Criar();

		(await usuarios.CadastrarAsync(Tenant, new(email, null, [], true))).Error.Should().Be(IntranetErrors.Acesso.EmailInvalido);
		gestao.Cadastrados.Should().BeEmpty();
	}

	[Fact]
	public async Task Cadastrar_SoPeloEntraSemFederacao_Recusado()
	{
		var (usuarios, _, gestao, _) = Criar(federacaoHabilitada: false);

		(await usuarios.CadastrarAsync(Tenant, new("dani@compras.com", null, [], EntraPorSenha: false)))
			.Error.Should().Be(IntranetErrors.Acesso.EntraSemFederacao);
		gestao.Cadastrados.Should().BeEmpty();
	}

	[Fact]
	public async Task Cadastrar_SoPeloEntraComFederacao_Aceito()
	{
		var (usuarios, _, gestao, _) = Criar(federacaoHabilitada: true);

		(await usuarios.CadastrarAsync(Tenant, new("dani@compras.com", null, [], EntraPorSenha: false))).IsSuccess.Should().BeTrue();
		gestao.Cadastrados.Should().ContainSingle().Which.EntraPorSenha.Should().BeFalse();
	}

	[Fact]
	public async Task Cadastrar_PerfilReservadoOuInexistente_Recusado()
	{
		var (usuarios, _, _, _) = Criar();

		(await usuarios.CadastrarAsync(Tenant, new("d@compras.com", null, ["installation-operator"], true)))
			.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		(await usuarios.CadastrarAsync(Tenant, new("d@compras.com", null, ["nao-existe"], true)))
			.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task Cadastrar_NormalizaPerfisENome_TrilhaSemEmail()
	{
		var (usuarios, _, gestao, trilha) = Criar();

		var id = await usuarios.CadastrarAsync(
			Tenant, new(" Dani@Compras.com ", "  Dani  ", [" compras-leitor", "compras-leitor", "compras-aprovador "], true));

		id.IsSuccess.Should().BeTrue();
		var cadastrado = gestao.Cadastrados.Should().ContainSingle().Subject;
		cadastrado.Email.Should().Be("Dani@Compras.com");
		cadastrado.Nome.Should().Be("Dani");
		cadastrado.Perfis.Should().Equal("compras-leitor", "compras-aprovador");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoUsuarioCadastrar);
		registro.Metadata.Should().NotContain("Dani@Compras.com").And.Contain(id.Value.Id.ToString());
	}

	[Fact]
	public async Task Cadastrar_NomeLongoDemais_Recusado()
	{
		var (usuarios, _, _, _) = Criar();

		(await usuarios.CadastrarAsync(Tenant, new("d@compras.com", new string('x', NomeDeExibicao.MaxLength + 1), [], true)))
			.IsFailure.Should().BeTrue();
	}

	[Fact]
	public async Task ReenviarConvite_SoParaQuemEntraPorSenhaESemSenha()
	{
		var (usuarios, _, gestao, _) = Criar();

		(await usuarios.ReenviarConviteAsync(Tenant, ComSenha)).Error.Should().Be(IntranetErrors.Acesso.ConviteNaoCabe);
		(await usuarios.ReenviarConviteAsync(Tenant, SoEntra)).Error.Should().Be(IntranetErrors.Acesso.ConviteNaoCabe);
		(await usuarios.ReenviarConviteAsync(Tenant, SemSenha)).IsSuccess.Should().BeTrue();

		gestao.Chamadas.Should().ContainSingle(c => c.StartsWith("reenviarConvite", StringComparison.Ordinal))
			.Which.Should().Be($"reenviarConvite:{Tenant}:{SemSenha}");
	}

	[Fact]
	public async Task Atribuir_Reservado_Recusado_EAtribuivelVaiParaATrilha()
	{
		var (usuarios, _, _, trilha) = Criar();

		(await usuarios.AtribuirPerfilAsync(Tenant, ComSenha, "installation-operator")).Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		(await usuarios.AtribuirPerfilAsync(Tenant, ComSenha, "compras-leitor")).IsSuccess.Should().BeTrue();

		trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoPerfilAtribuir);
	}

	[Fact]
	public async Task Federacao_TrocaSemConfirmacao_Recusada()
	{
		var (_, federacao, gestao, _) = Criar(federacaoHabilitada: true);

		(await federacao.DefinirAsync(Tenant, new(Guid.NewGuid().ToString(), true, ConfirmarTroca: false)))
			.Error.Should().Be(IntranetErrors.Acesso.TrocaDeDiretorioNaoConfirmada);
		gestao.Chamadas.Should().NotContain(c => c.StartsWith("definirFederacao", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Federacao_MesmoDiretorioOuPrimeiraVez_NaoPedeConfirmacao()
	{
		var (_, comDiretorio, _, _) = Criar(federacaoHabilitada: true);
		var (_, semDiretorio, _, _) = Criar(federacaoHabilitada: false);

		(await comDiretorio.DefinirAsync(Tenant, new(Diretorio.ToString(), false, false))).IsSuccess.Should().BeTrue();
		(await semDiretorio.DefinirAsync(Tenant, new(Guid.NewGuid().ToString(), true, false))).IsSuccess.Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("nao-e-guid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task Federacao_DirectoryIdInvalido(string? directoryId)
	{
		var (_, federacao, _, _) = Criar();

		(await federacao.DefinirAsync(Tenant, new(directoryId, true, true))).Error.Should().Be(IntranetErrors.Acesso.DirectoryIdInvalido);
	}

	[Fact]
	public async Task Retirar_ReservadoOuVazio_Recusado_EComumVaiParaATrilha()
	{
		var (usuarios, _, gestao, trilha) = Criar();
		await usuarios.AtribuirPerfilAsync(Tenant, ComSenha, "compras-leitor");
		trilha.Registros.Clear();

		(await usuarios.RetirarPerfilAsync(Tenant, ComSenha, "installation-operator")).Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		(await usuarios.RetirarPerfilAsync(Tenant, ComSenha, "  ")).Error.Should().Be(IntranetErrors.Acesso.PerfilRequired);
		(await usuarios.RetirarPerfilAsync(Tenant, ComSenha, " compras-leitor ")).IsSuccess.Should().BeTrue();

		gestao.Chamadas.Should().Contain($"retirar:{Tenant}:{ComSenha}:compras-leitor");
		trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoPerfilRetirar);
	}

	[Fact]
	public async Task ContaEsessoes_CadaAcaoRegistraOProprioVerbo()
	{
		var (usuarios, _, _, trilha) = Criar();

		await usuarios.DesativarAsync(Tenant, ComSenha);
		await usuarios.ReativarAsync(Tenant, ComSenha);
		await usuarios.EncerrarSessoesAsync(Tenant, ComSenha);

		trilha.Registros.Select(r => r.Verbo).Should().Equal(
			VerbosDeAuditoria.TenantAcessoUsuarioDesativar,
			VerbosDeAuditoria.TenantAcessoUsuarioReativar,
			VerbosDeAuditoria.TenantAcessoSessoesEncerrar);
	}

	[Fact]
	public async Task FalhaDaPlataforma_NaoGravaNaTrilha()
	{
		var (usuarios, federacao, gestao, trilha) = Criar(federacaoHabilitada: true);
		gestao.FalharCom = IntranetErrors.Acesso.Indisponivel;

		(await usuarios.DesativarAsync(Tenant, ComSenha)).IsFailure.Should().BeTrue();
		(await usuarios.CadastrarAsync(Tenant, new("d@compras.com", null, [], true))).IsFailure.Should().BeTrue();
		(await federacao.DefinirAsync(Tenant, new(Diretorio.ToString(), true, false))).IsFailure.Should().BeTrue();

		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Federacao_Definida_VaiParaATrilhaComODiretorio()
	{
		var (_, federacao, _, trilha) = Criar();
		var diretorio = Guid.NewGuid();

		(await federacao.DefinirAsync(Tenant, new(diretorio.ToString(), true, false))).IsSuccess.Should().BeTrue();

		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.TenantAcessoFederacaoDefinir);
		registro.Metadata.Should().Contain(diretorio.ToString());
	}

	[Fact]
	public async Task Cadastrar_NomeLongoDemais_RecusadoComoCadastroRecusado()
	{
		var (usuarios, _, gestao, _) = Criar();

		(await usuarios.CadastrarAsync(Tenant, new("d@compras.com", new string('x', NomeDeExibicao.MaxLength + 1), [], true)))
			.Error.Should().Be(IntranetErrors.Acesso.CadastroRecusado);
		gestao.Cadastrados.Should().BeEmpty();
	}

	[Fact]
	public async Task Cadastrar_EmailComContaNaInstalacao_IncluiComoMembro_ComOsPerfis()
	{
		var (usuarios, _, gestao, trilha) = Criar();
		var daIntranet = Guid.NewGuid();
		gestao.ContasDaInstalacao["eva@intranet.com"] = daIntranet;

		var cadastro = await usuarios.CadastrarAsync(Tenant, new("eva@intranet.com", null, ["compras-leitor"], true));

		cadastro.Value.Should().Be(new CadastroNoTenantDto(daIntranet, IncluidaComoMembro: true));
		gestao.Cadastrados.Should().BeEmpty("a credencial da pessoa não muda: ela já tem conta");
		gestao.VinculoDe(daIntranet).Should().Be(VinculoNoTenant.Membro);
		gestao.Chamadas.Should().Contain($"atribuir:{Tenant}:{daIntranet}:compras-leitor");
		trilha.Registros.Select(r => r.Verbo).Should().Contain(VerbosDeAuditoria.TenantAcessoVinculoIncluir);
		trilha.Registros.Should().OnlyContain(r => !r.Metadata!.Contains("eva@intranet.com"));
	}

	[Fact]
	public async Task Cadastrar_QuemJaEMembro_Recusa()
	{
		var (usuarios, _, _, _) = Criar();

		(await usuarios.CadastrarAsync(Tenant, new("ana@compras.com", null, [], true))).Error.Should().Be(IntranetErrors.Acesso.JaEMembro);
	}

	[Fact]
	public async Task AcoesDeConta_EmPessoaDeOutroSistema_RecusadasSemChamarAPlataforma()
	{
		var (usuarios, _, gestao, _) = Criar();
		var membro = Guid.NewGuid();
		gestao.ComMembro(membro, "eva@intranet.com");

		(await usuarios.DesativarAsync(Tenant, membro)).Error.Should().Be(IntranetErrors.Acesso.ContaDeOutroSistema);
		(await usuarios.ReativarAsync(Tenant, membro)).Error.Should().Be(IntranetErrors.Acesso.ContaDeOutroSistema);
		(await usuarios.EncerrarSessoesAsync(Tenant, membro)).Error.Should().Be(IntranetErrors.Acesso.ContaDeOutroSistema);
		(await usuarios.ReenviarConviteAsync(Tenant, membro)).Error.Should().Be(IntranetErrors.Acesso.ContaDeOutroSistema);

		gestao.Chamadas.Should().NotContain(c => c.StartsWith("desativar", StringComparison.Ordinal)
			|| c.StartsWith("reativar:", StringComparison.Ordinal)
			|| c.StartsWith("encerrarSessoes", StringComparison.Ordinal)
			|| c.StartsWith("reenviarConvite", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Vinculo_SuspenderReativarRemover_ComTrilha()
	{
		var (usuarios, _, gestao, trilha) = Criar();
		var membro = Guid.NewGuid();
		gestao.ComMembro(membro, "eva@intranet.com");

		(await usuarios.SuspenderVinculoAsync(Tenant, membro)).IsSuccess.Should().BeTrue();
		gestao.VinculoDe(membro).Should().Be(VinculoNoTenant.Suspenso);
		(await usuarios.ReativarVinculoAsync(Tenant, membro)).IsSuccess.Should().BeTrue();
		gestao.VinculoDe(membro).Should().Be(VinculoNoTenant.Membro);
		(await usuarios.RemoverVinculoAsync(Tenant, membro)).IsSuccess.Should().BeTrue();
		gestao.EstaNoTenant(membro).Should().BeFalse();

		trilha.Registros.Select(r => r.Verbo).Should().Equal(
			VerbosDeAuditoria.TenantAcessoVinculoSuspender,
			VerbosDeAuditoria.TenantAcessoVinculoReativar,
			VerbosDeAuditoria.TenantAcessoVinculoRemover);
	}

	[Fact]
	public async Task Vinculo_DoDono_NaoSeSuspendeNemRemove()
	{
		var (usuarios, _, gestao, _) = Criar();

		(await usuarios.SuspenderVinculoAsync(Tenant, ComSenha)).Error.Should().Be(IntranetErrors.Acesso.VinculoDoDono);
		(await usuarios.RemoverVinculoAsync(Tenant, ComSenha)).Error.Should().Be(IntranetErrors.Acesso.VinculoDoDono);
		gestao.Chamadas.Should().NotContain(c => c.Contains("Vinculo", StringComparison.Ordinal));
	}
}
