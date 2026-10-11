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
		registro.Metadata.Should().NotContain("Dani@Compras.com").And.Contain(id.Value.ToString());
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
}
