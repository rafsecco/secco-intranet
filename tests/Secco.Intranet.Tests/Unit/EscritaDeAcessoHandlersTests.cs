using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class EscritaDeAcessoHandlersTests
{
	private static readonly Guid Admin = Guid.NewGuid();
	private static readonly Guid OutroAdmin = Guid.NewGuid();
	private static readonly Guid Ana = Guid.NewGuid();

	private static GestaoDeAcessoFalsa Cenario() => new GestaoDeAcessoFalsa()
		.ComPerfil("intranet-admin").ComPerfil("marketing-admin").ComPerfil("gerente-de-compras").ComPerfil("installation-operator", reservado: true)
		.ComUsuario(Admin, "admin@x.com", SituacaoDoUsuario.Ativo, "intranet-admin")
		.ComUsuario(Ana, "ana@x.com");

	// --- CriarPerfil ---

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("gerente de compras")]
	[InlineData("-x")]
	public async Task CriarPerfil_NomeInvalido_RecusaSemChamarAPlataforma(string nome)
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new CriarPerfilHandler(gestao, trilha).HandleAsync(nome);

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNomeInvalido);
		gestao.Chamadas.Should().BeEmpty();
		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task CriarPerfil_Com101Caracteres_Recusa()
	{
		var resultado = await new CriarPerfilHandler(Cenario(), new TrilhaDeAcessoFalsa()).HandleAsync(new string('a', 101));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNomeInvalido);
	}

	[Theory]
	[InlineData("installation-operator")]
	[InlineData("Installation-Auditor")]
	public async Task CriarPerfil_Reservado_Recusa(string nome)
	{
		var gestao = Cenario();

		var resultado = await new CriarPerfilHandler(gestao, new TrilhaDeAcessoFalsa()).HandleAsync(nome);

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task CriarPerfil_Valido_CriaEAudita()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new CriarPerfilHandler(gestao, trilha).HandleAsync("  equipe-financeiro ");

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal("perfil-criar:equipe-financeiro");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.AcessoPerfilCriar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Acesso);
		registro.RecursoId.Should().Be("equipe-financeiro");
	}

	[Fact]
	public async Task CriarPerfil_QuandoAPlataformaFalha_NaoAudita()
	{
		var gestao = Cenario();
		gestao.FalharCom = IntranetErrors.Acesso.Indisponivel;
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new CriarPerfilHandler(gestao, trilha).HandleAsync("equipe-financeiro");

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		trilha.Registros.Should().BeEmpty("a ação não aconteceu, então não entra na trilha");
	}

	// --- ExcluirPerfil ---

	[Theory]
	[InlineData("intranet-admin")]
	[InlineData("Inventario-Admin")]
	[InlineData("marketing-admin")]
	[InlineData("rh-user")]
	public async Task ExcluirPerfil_DoProdutoOuDeSetor_Protegido(string nome)
	{
		var gestao = Cenario();

		var resultado = await new ExcluirPerfilHandler(gestao, new TrilhaDeAcessoFalsa()).HandleAsync(nome);

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilProtegido);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task ExcluirPerfil_Reservado_Recusa()
	{
		var resultado = await new ExcluirPerfilHandler(Cenario(), new TrilhaDeAcessoFalsa()).HandleAsync("installation-operator");

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
	}

	[Fact]
	public async Task ExcluirPerfil_Comum_ExcluiEAudita()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new ExcluirPerfilHandler(gestao, trilha).HandleAsync("gerente-de-compras");

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal("perfil-excluir:gerente-de-compras");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoPerfilExcluir);
	}

	// --- AtribuirPerfil ---

	[Fact]
	public async Task AtribuirPerfil_Reservado_RecusaSemChamarAPlataforma()
	{
		var gestao = Cenario();

		var resultado = await new AtribuirPerfilHandler(gestao, new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(Ana, "Installation-Operator"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task AtribuirPerfil_MarcadoComoReservadoPelaPlataforma_Recusa()
	{
		// Um reservado que a lista local não conhece: a decisão definitiva vem do IsReserved.
		var gestao = Cenario().ComPerfil("novo-reservado", reservado: true);

		var resultado = await new AtribuirPerfilHandler(gestao, new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(Ana, "novo-reservado"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilReservado);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task AtribuirPerfil_PerfilInexistente_DevolveNotFound()
	{
		var resultado = await new AtribuirPerfilHandler(Cenario(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(Ana, "nao-existe"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task AtribuirPerfil_UsuarioInexistente_DevolveNotFound()
	{
		var resultado = await new AtribuirPerfilHandler(Cenario(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new AtribuirPerfilCommand(Guid.NewGuid(), "gerente-de-compras"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}

	[Fact]
	public async Task AtribuirPerfil_Valido_AtribuiEAuditaComEmailDoAfetado()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new AtribuirPerfilHandler(gestao, trilha)
			.HandleAsync(new AtribuirPerfilCommand(Ana, "gerente-de-compras"));

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"perfil-atribuir:{Ana}:gerente-de-compras");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.AcessoPerfilAtribuir);
		registro.Metadata.Should().Contain("gerente-de-compras").And.Contain("ana@x.com").And.Contain(Ana.ToString());
	}

	// --- RetirarPerfil ---

	[Fact]
	public async Task RetirarPerfil_IntranetAdminDeSiMesmo_Recusa()
	{
		var gestao = Cenario().ComUsuario(OutroAdmin, "outro@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Admin))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.AutoRemocaoDeIntranetAdmin);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task RetirarPerfil_UltimoIntranetAdminAtivo_Recusa()
	{
		var gestao = Cenario();

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(OutroAdmin))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task RetirarPerfil_OutroAdminDesativadoNaoConta_ComoRestante()
	{
		var gestao = Cenario().ComUsuario(OutroAdmin, "outro@x.com", SituacaoDoUsuario.Desativado, "intranet-admin");

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Guid.NewGuid()))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin,
			"um intranet-admin desativado não abre a área — não pode ser o 'outro' que sobra");
	}

	[Fact]
	public async Task RetirarPerfil_ComOutroAdminAtivo_Retira()
	{
		var gestao = Cenario().ComUsuario(OutroAdmin, "outro@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new RetirarPerfilHandler(gestao, trilha, new AtorDeAcessoFalso(OutroAdmin))
			.HandleAsync(new RetirarPerfilCommand(Admin, "Intranet-Admin"));

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"perfil-retirar:{Admin}:Intranet-Admin");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoPerfilRetirar);
	}

	[Fact]
	public async Task RetirarPerfil_ContaOsAdminsAtivosEmTodasAsPaginas()
	{
		// O único outro admin ativo está na quarta página da API: contar só a primeira daria falso "último".
		var gestao = Cenario().ComUsuario(Guid.NewGuid(), "d1@x.com", SituacaoDoUsuario.Desativado, "intranet-admin")
			.ComUsuario(Guid.NewGuid(), "d2@x.com", SituacaoDoUsuario.Desativado, "intranet-admin")
			.ComUsuario(OutroAdmin, "outro@x.com", SituacaoDoUsuario.Ativo, "intranet-admin");
		gestao.TamanhoDaPagina = 1;

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Guid.NewGuid()))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task RetirarPerfil_PerfilQueNaoEIntranetAdmin_NaoAplicaARegraDoUltimo()
	{
		var gestao = Cenario().ComUsuario(Guid.NewGuid(), "z@x.com", SituacaoDoUsuario.Ativo, "gerente-de-compras");

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Admin))
			.HandleAsync(new RetirarPerfilCommand(Admin, "gerente-de-compras"));

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task RetirarPerfil_SemAtorIdentificado_UsaSoARegraDoUltimo()
	{
		// Modo aberto de DEV: não há ator. A regra do último admin continua valendo.
		var gestao = Cenario();

		var resultado = await new RetirarPerfilHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(null))
			.HandleAsync(new RetirarPerfilCommand(Admin, "intranet-admin"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin);
	}

	// --- Desativar / Reativar / Encerrar sessões ---

	[Fact]
	public async Task DesativarUsuario_ASiMesmo_Recusa()
	{
		var gestao = Cenario();

		var resultado = await new DesativarUsuarioHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Admin))
			.HandleAsync(Admin);

		resultado.Error.Should().Be(IntranetErrors.Acesso.AutoDesativacao);
		gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task DesativarUsuario_UltimoIntranetAdminAtivo_Recusa()
	{
		var gestao = Cenario();

		var resultado = await new DesativarUsuarioHandler(gestao, new TrilhaDeAcessoFalsa(), new AtorDeAcessoFalso(Guid.NewGuid()))
			.HandleAsync(Admin);

		resultado.Error.Should().Be(IntranetErrors.Acesso.UltimoIntranetAdmin);
	}

	[Fact]
	public async Task DesativarUsuario_Comum_DesativaEAudita()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new DesativarUsuarioHandler(gestao, trilha, new AtorDeAcessoFalso(Admin)).HandleAsync(Ana);

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"usuario-desativar:{Ana}");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoUsuarioDesativar);
		trilha.Registros.Single().Metadata.Should().Contain("ana@x.com");
	}

	[Fact]
	public async Task ReativarUsuario_ReativaEAudita()
	{
		var gestao = Cenario().ComUsuario(Guid.NewGuid(), "z@x.com");
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new ReativarUsuarioHandler(gestao, trilha).HandleAsync(Ana);

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"usuario-reativar:{Ana}");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoUsuarioReativar);
	}

	[Fact]
	public async Task EncerrarSessoes_EncerraEAudita_MesmoDeSiMesmo()
	{
		var gestao = Cenario();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await new EncerrarSessoesHandler(gestao, trilha).HandleAsync(Admin);

		resultado.IsSuccess.Should().BeTrue();
		gestao.Chamadas.Should().Equal($"sessoes-encerrar:{Admin}");
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.AcessoSessoesEncerrar);
	}

	[Fact]
	public async Task EncerrarSessoes_UsuarioInexistente_DevolveNotFound()
	{
		var resultado = await new EncerrarSessoesHandler(Cenario(), new TrilhaDeAcessoFalsa()).HandleAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}
}
