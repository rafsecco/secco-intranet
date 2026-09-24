using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ImportarDiretorioHandlerTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();

	private const string Cabecalho = "email;nome;cargo;ramal;setor;gestor\n";

	private sealed record Ambiente(
		ImportarDiretorioHandler Handler,
		PerfisColaboradorFalso Perfis,
		TrilhaDeAcessoFalsa Trilha,
		SetoresFalsos Setores,
		UsuariosParaDiretorioFalso Usuarios);

	private static Ambiente Montar(Action<SetoresFalsos>? setores = null, Action<PerfisColaboradorFalso>? perfis = null)
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com").Com(Carla, "carla@x.com");
		var setoresFalsos = new SetoresFalsos();
		setores?.Invoke(setoresFalsos);
		var perfisFalsos = new PerfisColaboradorFalso();
		perfis?.Invoke(perfisFalsos);
		var trilha = new TrilhaDeAcessoFalsa();

		return new Ambiente(new ImportarDiretorioHandler(usuarios, perfisFalsos, setoresFalsos, trilha), perfisFalsos, trilha, setoresFalsos, usuarios);
	}

	[Fact]
	public async Task Previsualizar_NaoGravaNemAudita_MasDiz_O_Que_Faria()
	{
		var ambiente = Montar(setores => setores.Com("Financeiro", "financeiro"));

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana Ribeiro;Controller;2100;financeiro;\nbruno@x.com;Bruno;;;;ana@x.com\n"));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Aplicado.Should().BeFalse();
		resultado.Value.Criados.Should().Be(2);
		ambiente.Perfis.Perfis.Should().BeEmpty();
		ambiente.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Aplicar_CriaOsPerfisEAuditaUmaVezComOsTotais()
	{
		var ambiente = Montar(setores => setores.Com("Financeiro", "financeiro"));

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana Ribeiro;Controller;2100;financeiro;\nbruno@x.com;Bruno;;;;ana@x.com\n"));

		resultado.Value.Aplicado.Should().BeTrue();
		ambiente.Perfis.Perfis.Should().HaveCount(2);
		var ana = ambiente.Perfis.Perfis.Single(p => p.UsuarioId == Ana);
		ana.NomeExibicao.Should().Be("Ana Ribeiro");
		ana.Cargo.Should().Be("Controller");
		ana.SetorId.Should().NotBeNull();
		ambiente.Perfis.Perfis.Single(p => p.UsuarioId == Bruno).GestorUsuarioId.Should().Be(Ana);

		var registro = ambiente.Trilha.Registros.Should().ContainSingle("um registro por importação, não por linha").Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.DiretorioImportar);
		registro.Metadata.Should().Contain("\"criados\":2");
		registro.Metadata.Should().NotContain("Ana Ribeiro");
	}

	[Fact]
	public async Task Reimportar_OMesmoArquivo_NaoMudaNada()
	{
		var ambiente = Montar();
		var csv = new ImportarDiretorioCommand(Cabecalho + "ana@x.com;Ana;Diretora;2100;;\n");
		await ambiente.Handler.AplicarAsync(csv);

		var segunda = await ambiente.Handler.AplicarAsync(csv);

		segunda.Value.SemAlteracao.Should().Be(1);
		segunda.Value.Criados.Should().Be(0);
		segunda.Value.Atualizados.Should().Be(0);
	}

	[Fact]
	public async Task CelulaVazia_MantemOValorAtual_NaoLimpa()
	{
		var existente = new PerfilColaborador(Ana);
		existente.EditarContato("Ana", "2100", null);
		existente.EditarDadosFuncionais("Diretora", null, Bruno);
		var ambiente = Montar(perfis: perfis => perfis.Com(existente));

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;Presidente;;;\n"));

		resultado.Value.Atualizados.Should().Be(1);
		existente.NomeExibicao.Should().Be("Ana");
		existente.Ramal.Should().Be("2100");
		existente.Cargo.Should().Be("Presidente");
		existente.GestorUsuarioId.Should().Be(Bruno, "gestor vazio no CSV não limpa o gestor atual");
	}

	[Fact]
	public async Task LinhaSoComEmail_SemPerfil_NaoCriaPerfilVazio()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;;;;\n"));

		resultado.Value.SemAlteracao.Should().Be(1);
		ambiente.Perfis.Perfis.Should().BeEmpty();
	}

	[Fact]
	public async Task EmailQueNaoEUsuarioAtivo_ErroDaLinha_OutrasLinhasSeguem()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "fantasma@x.com;Fantasma;;;;\nana@x.com;Ana;;;;\n"));

		resultado.Value.ComErro.Should().Be(1);
		resultado.Value.Linhas.Single(l => l.Status == StatusDaLinha.Erro).Erro.Should().Contain("usuário ativo");
		ambiente.Perfis.Perfis.Should().ContainSingle(p => p.UsuarioId == Ana);
	}

	[Fact]
	public async Task EmailRepetidoNoArquivo_SegundaLinhaDaErro()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana;;;;\nANA@x.com;Outra;;;;\n"));

		resultado.Value.Linhas[0].Status.Should().Be(StatusDaLinha.Criar);
		resultado.Value.Linhas[1].Status.Should().Be(StatusDaLinha.Erro);
		resultado.Value.Linhas[1].Erro.Should().Contain("repetido");
	}

	[Fact]
	public async Task SetorInexistenteOuInativo_ErroDaLinha()
	{
		var ambiente = Montar(setores => setores.Com("Antigo", "antigo", ativo: false));

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;;;;nao-existe;\nbruno@x.com;;;;antigo;\n"));

		resultado.Value.ComErro.Should().Be(2);
	}

	[Fact]
	public async Task GestorQueNaoEUsuarioAtivo_EAutogestor_ErroDaLinha()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;;;;;fantasma@x.com\nbruno@x.com;;;;;bruno@x.com\n"));

		resultado.Value.Linhas.Should().OnlyContain(l => l.Status == StatusDaLinha.Erro);
	}

	[Fact]
	public async Task GestorQueSoApareceMaisAbaixoNoArquivo_Funciona()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "bruno@x.com;Bruno;;;;ana@x.com\nana@x.com;Ana;;;;\n"));

		resultado.Value.ComErro.Should().Be(0);
		ambiente.Perfis.Perfis.Single(p => p.UsuarioId == Bruno).GestorUsuarioId.Should().Be(Ana);
	}

	[Fact]
	public async Task CicloDentroDoMesmoLote_AUltimaLinhaQueOFechaDaErro()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "bruno@x.com;;;;;ana@x.com\nana@x.com;;;;;bruno@x.com\n"));

		resultado.Value.Linhas[0].Status.Should().Be(StatusDaLinha.Criar);
		resultado.Value.Linhas[1].Status.Should().Be(StatusDaLinha.Erro);
		resultado.Value.Linhas[1].Erro.Should().Contain("ciclo");
		ambiente.Perfis.Perfis.Should().ContainSingle(p => p.UsuarioId == Bruno);
	}

	[Fact]
	public async Task CicloComPerfilJaExistenteNoBanco_Recusa()
	{
		var bruno = new PerfilColaborador(Bruno);
		bruno.EditarDadosFuncionais(null, null, Ana);
		var ambiente = Montar(perfis: perfis => perfis.Com(bruno));

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;;;;bruno@x.com\n"));

		resultado.Value.Linhas.Single().Status.Should().Be(StatusDaLinha.Erro);
	}

	[Fact]
	public async Task CamposAcimaDoLimite_ErroDaLinha()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + $"ana@x.com;{new string('n', PerfilColaborador.NomeMaxLength + 1)};;;;\n"));

		resultado.Value.Linhas.Single().Status.Should().Be(StatusDaLinha.Erro);
	}

	[Fact]
	public async Task ArquivoInvalido_FalhaSemGravarNada()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand("email;salario\nana@x.com;1\n"));

		resultado.IsFailure.Should().BeTrue();
		ambiente.Perfis.Perfis.Should().BeEmpty();
		ambiente.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task SecureGateForaDoAr_PropagaOErro()
	{
		var ambiente = Montar();
		ambiente.Usuarios.FalharCom = IntranetErrors.Acesso.Indisponivel;

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;Ana;;;;\n"));

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task ErrosDoLeitor_AparecemNoRelatorioComONumeroDaLinha()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(Cabecalho + ";Sem email;;;;\nana@x.com;Ana;;;;\n"));

		resultado.Value.Linhas.Should().Contain(l => l.Numero == 2 && l.Status == StatusDaLinha.Erro);
		resultado.Value.Linhas.Should().Contain(l => l.Numero == 3 && l.Status == StatusDaLinha.Criar);
	}
}
