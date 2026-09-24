using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class EscritaDoDiretorioHandlersTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();
	private static readonly Guid Fora = Guid.NewGuid();

	private static UsuariosParaDiretorioFalso Usuarios() =>
		new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com").Com(Carla, "carla@x.com");

	private static EditarContatoHandler Contato(UsuariosParaDiretorioFalso usuarios, PerfisColaboradorFalso perfis, TrilhaDeAcessoFalsa trilha) =>
		new(usuarios, perfis, trilha);

	private static EditarDadosFuncionaisHandler Funcionais(
		UsuariosParaDiretorioFalso usuarios, PerfisColaboradorFalso perfis, SetoresFalsos setores, TrilhaDeAcessoFalsa trilha) =>
		new(usuarios, perfis, setores, trilha);

	// --- Contato ---

	[Fact]
	public async Task Contato_PrimeiraEdicao_CriaOPerfilEAuditaSoNomesDeCampo()
	{
		var perfis = new PerfisColaboradorFalso();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await Contato(Usuarios(), perfis, trilha)
			.HandleAsync(new EditarContatoCommand(Ana, "Ana Ribeiro", "2100", null));

		resultado.IsSuccess.Should().BeTrue();
		perfis.Perfis.Should().ContainSingle().Which.NomeExibicao.Should().Be("Ana Ribeiro");
		var registro = trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DiretorioPerfilEditar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Diretorio);
		registro.RecursoId.Should().Be(Ana.ToString());
		registro.Metadata.Should().Contain("nome").And.Contain("ramal");
		registro.Metadata.Should().NotContain("Ana Ribeiro", "a trilha guarda o nome do campo, não o valor");
		registro.Metadata.Should().NotContain("2100");
	}

	[Fact]
	public async Task Contato_SemMudanca_NaoCriaPerfilVazioNemAudita()
	{
		var perfis = new PerfisColaboradorFalso();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await Contato(Usuarios(), perfis, trilha).HandleAsync(new EditarContatoCommand(Ana, null, "  ", ""));

		resultado.IsSuccess.Should().BeTrue();
		perfis.Perfis.Should().BeEmpty("um perfil sem nada não deve ser gravado");
		trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Contato_PerfilExistente_Atualiza()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarContato("Ana", null, null);
		var perfis = new PerfisColaboradorFalso().Com(existente);

		var resultado = await Contato(Usuarios(), perfis, new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarContatoCommand(Ana, "Ana Ribeiro", null, null));

		resultado.IsSuccess.Should().BeTrue();
		existente.NomeExibicao.Should().Be("Ana Ribeiro");
		perfis.Salvou.Should().Be(1);
	}

	[Fact]
	public async Task Contato_UsuarioDesativadoOuInexistente_NotFound()
	{
		var resultado = await Contato(Usuarios(), new PerfisColaboradorFalso(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarContatoCommand(Fora, "Fantasma", null, null));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.PessoaNaoEncontrada);
	}

	[Theory]
	[InlineData(PerfilColaborador.NomeMaxLength + 1, 0, 0)]
	[InlineData(0, PerfilColaborador.RamalMaxLength + 1, 0)]
	[InlineData(0, 0, PerfilColaborador.SobreMaxLength + 1)]
	public async Task Contato_AcimaDoLimite_RecusaSemGravar(int nome, int ramal, int sobre)
	{
		var perfis = new PerfisColaboradorFalso();

		var resultado = await Contato(Usuarios(), perfis, new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarContatoCommand(Ana, new string('n', nome), new string('1', ramal), new string('s', sobre)));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Type.Should().Be(Secco.SharedKernel.Results.ErrorType.Validation);
		perfis.Perfis.Should().BeEmpty();
	}

	[Fact]
	public async Task Contato_SecureGateForaDoAr_PropagaOErro()
	{
		var usuarios = Usuarios();
		usuarios.FalharCom = IntranetErrors.Acesso.Indisponivel;

		var resultado = await Contato(usuarios, new PerfisColaboradorFalso(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarContatoCommand(Ana, "Ana", null, null));

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Contato_NuncaMexeEmDadosFuncionais()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarDadosFuncionais("Diretora", null, Bruno);
		var perfis = new PerfisColaboradorFalso().Com(existente);

		await Contato(Usuarios(), perfis, new TrilhaDeAcessoFalsa()).HandleAsync(new EditarContatoCommand(Ana, "Ana", null, null));

		existente.Cargo.Should().Be("Diretora");
		existente.GestorUsuarioId.Should().Be(Bruno);
	}

	// --- Dados funcionais ---

	[Fact]
	public async Task Funcionais_DefineCargoSetorEGestor_EAudita()
	{
		var setores = new SetoresFalsos();
		var financeiro = setores.Com("Financeiro", "financeiro");
		var perfis = new PerfisColaboradorFalso();
		var trilha = new TrilhaDeAcessoFalsa();

		var resultado = await Funcionais(Usuarios(), perfis, setores, trilha)
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, "Controller", financeiro.Id, Bruno));

		resultado.IsSuccess.Should().BeTrue();
		var perfil = perfis.Perfis.Single();
		perfil.Cargo.Should().Be("Controller");
		perfil.SetorId.Should().Be(financeiro.Id);
		perfil.GestorUsuarioId.Should().Be(Bruno);
		trilha.Registros.Single().Verbo.Should().Be(VerbosDeAuditoria.DiretorioDadosFuncionaisEditar);
		trilha.Registros.Single().Metadata.Should().Contain("cargo").And.Contain("setor").And.Contain("gestor");
	}

	[Fact]
	public async Task Funcionais_GestorEhOProprio_Recusa()
	{
		var resultado = await Funcionais(Usuarios(), new PerfisColaboradorFalso(), new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, Ana));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.GestorEhOProprio);
	}

	[Fact]
	public async Task Funcionais_GestorQueNaoEUsuarioAtivo_Recusa()
	{
		var resultado = await Funcionais(Usuarios(), new PerfisColaboradorFalso(), new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, Fora));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.GestorInvalido);
	}

	[Fact]
	public async Task Funcionais_CicloDireto_Recusa()
	{
		var bruno = new PerfilColaborador(Bruno);
		bruno.EditarDadosFuncionais(null, null, Ana);
		var perfis = new PerfisColaboradorFalso().Com(bruno);

		var resultado = await Funcionais(Usuarios(), perfis, new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, Bruno));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.GestorCriariaCiclo);
		perfis.Perfis.Should().ContainSingle("nada foi criado para a Ana");
	}

	[Fact]
	public async Task Funcionais_CicloDeTresPessoas_Recusa()
	{
		var bruno = new PerfilColaborador(Bruno);
		bruno.EditarDadosFuncionais(null, null, Ana);
		var carla = new PerfilColaborador(Carla);
		carla.EditarDadosFuncionais(null, null, Bruno);
		var perfis = new PerfisColaboradorFalso().Com(bruno).Com(carla);

		var resultado = await Funcionais(Usuarios(), perfis, new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, Carla));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.GestorCriariaCiclo);
	}

	[Fact]
	public async Task Funcionais_SetorInexistente_Recusa()
	{
		var resultado = await Funcionais(Usuarios(), new PerfisColaboradorFalso(), new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, Guid.NewGuid(), null));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.SetorInvalido);
	}

	[Fact]
	public async Task Funcionais_SetorInativo_RecusadoParaLotacaoNova_MasMantidoSeJaEra()
	{
		var setores = new SetoresFalsos();
		var antigo = setores.Com("Antigo", "antigo", ativo: false);
		var perfis = new PerfisColaboradorFalso();
		var handler = Funcionais(Usuarios(), perfis, setores, new TrilhaDeAcessoFalsa());

		(await handler.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, antigo.Id, null)))
			.Error.Should().Be(IntranetErrors.Diretorio.SetorInvalido, "lotação nova exige setor ativo");

		var jaLotada = new PerfilColaborador(Bruno);
		jaLotada.EditarDadosFuncionais(null, antigo.Id, null);
		perfis.Com(jaLotada);

		(await handler.HandleAsync(new EditarDadosFuncionaisCommand(Bruno, "Novo cargo", antigo.Id, null)))
			.IsSuccess.Should().BeTrue("quem já estava lotado no setor desativado continua podendo ter o cargo editado");
	}

	[Fact]
	public async Task Funcionais_CargoAcimaDoLimite_Recusa()
	{
		var resultado = await Funcionais(Usuarios(), new PerfisColaboradorFalso(), new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, new string('c', PerfilColaborador.CargoMaxLength + 1), null, null));

		resultado.Error.Should().Be(IntranetErrors.Diretorio.CargoTooLong(PerfilColaborador.CargoMaxLength));
	}

	[Fact]
	public async Task Funcionais_NuncaMexeNoContato()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarContato("Ana", "2100", "Sobre");
		var perfis = new PerfisColaboradorFalso().Com(existente);

		await Funcionais(Usuarios(), perfis, new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, "Diretora", null, null));

		existente.NomeExibicao.Should().Be("Ana");
		existente.Ramal.Should().Be("2100");
		existente.Sobre.Should().Be("Sobre");
	}

	[Fact]
	public async Task Funcionais_LimparOGestor_Funciona()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarDadosFuncionais(null, null, Bruno);
		var perfis = new PerfisColaboradorFalso().Com(existente);

		var resultado = await Funcionais(Usuarios(), perfis, new SetoresFalsos(), new TrilhaDeAcessoFalsa())
			.HandleAsync(new EditarDadosFuncionaisCommand(Ana, null, null, null));

		resultado.IsSuccess.Should().BeTrue();
		existente.GestorUsuarioId.Should().BeNull();
	}
}
