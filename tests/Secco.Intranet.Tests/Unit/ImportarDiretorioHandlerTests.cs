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
		UsuariosParaDiretorioFalso Usuarios,
		GestaoDeAcessoFalsa Gestao);

	private static Ambiente Montar(Action<SetoresFalsos>? setores = null, Action<PerfisColaboradorFalso>? perfis = null)
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com").Com(Carla, "carla@x.com");
		var setoresFalsos = new SetoresFalsos();
		setores?.Invoke(setoresFalsos);
		var perfisFalsos = new PerfisColaboradorFalso();
		perfis?.Invoke(perfisFalsos);
		var trilha = new TrilhaDeAcessoFalsa();
		var gestao = new GestaoDeAcessoFalsa();

		return new Ambiente(
			new ImportarDiretorioHandler(usuarios, perfisFalsos, setoresFalsos, gestao, trilha), perfisFalsos, trilha, setoresFalsos, usuarios, gestao);
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
		ambiente.Gestao.Nomes[Ana].Should().Be("Ana Ribeiro", "o nome vai para o SecureGate");
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
		// Nome vazio: o dublê de usuários não reflete o que a gestão gravou, e o que se prova aqui é
		// o perfil local — o nome tem testes próprios mais abaixo.
		var csv = new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;Diretora;2100;;\n");
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
		existente.EditarContato("2100", null);
		existente.EditarDadosFuncionais("Diretora", null, Bruno);
		var ambiente = Montar(perfis: perfis => perfis.Com(existente));

		var resultado = await ambiente.Handler.AplicarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;Presidente;;;\n"));

		resultado.Value.Atualizados.Should().Be(1);
		ambiente.Gestao.Chamadas.Should().BeEmpty("nome vazio no CSV não mexe no nome");
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
			// Ana ganha cargo: linha só com nome não cria perfil local (o nome é do SecureGate).
			Cabecalho + "fantasma@x.com;Fantasma;;;;\nana@x.com;Ana;Analista;;;\n"));

		resultado.Value.ComErro.Should().Be(1);
		resultado.Value.Linhas.Single(l => l.Status == StatusDaLinha.Erro).Erro.Should().Contain("usuário ativo");
		ambiente.Perfis.Perfis.Should().ContainSingle(p => p.UsuarioId == Ana);
	}

	[Fact]
	public async Task EmailRepetidoNoArquivo_SegundaLinhaDaErro()
	{
		var ambiente = Montar();

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana;Analista;;;\nANA@x.com;Outra;;;;\n"));

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
			Cabecalho + $"ana@x.com;{new string('n', NomeDeExibicao.MaxLength + 1)};;;;\n"));

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

		var resultado = await ambiente.Handler.PrevisualizarAsync(new ImportarDiretorioCommand(Cabecalho + ";Sem email;;;;\nana@x.com;Ana;Analista;;;\n"));

		resultado.Value.Linhas.Should().Contain(l => l.Numero == 2 && l.Status == StatusDaLinha.Erro);
		resultado.Value.Linhas.Should().Contain(l => l.Numero == 3 && l.Status == StatusDaLinha.Criar);
	}

	[Fact]
	public async Task Importar_NomeQueMuda_VaiParaAPlataforma_ENomeIgualNao()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com", "Ana").Com(Bruno, "bruno@x.com", "Bruno");
		var gestao = new GestaoDeAcessoFalsa();
		var handler = Criar(usuarios, gestao);

		var relatorio = await handler.AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana Ribeiro;;;;\nbruno@x.com;Bruno;;;;\n"));

		gestao.Chamadas.Should().Equal($"usuario-nome:{Ana}:Ana Ribeiro");
		relatorio.Value.Atualizados.Should().Be(1);
		relatorio.Value.SemAlteracao.Should().Be(1);
		usuarios.Esquecimentos.Should().Be(1, "uma vez no fim do lote");
	}

	[Fact]
	public async Task Importar_NomeVazioNoCsv_NaoMexeNoNome()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com", "Ana");
		var gestao = new GestaoDeAcessoFalsa();

		await Criar(usuarios, gestao).AplicarAsync(new ImportarDiretorioCommand(Cabecalho + "ana@x.com;;Analista;;;\n"));

		gestao.Chamadas.Should().BeEmpty("célula vazia no CSV é 'não alterar'");
	}

	[Fact]
	public async Task Importar_PlataformaRecusaUmNome_LinhaViraErroEOLoteContinua()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com");
		var gestao = new GestaoDeAcessoFalsa { FalharCom = IntranetErrors.Acesso.Indisponivel };
		var perfis = new PerfisColaboradorFalso();

		var relatorio = await Criar(usuarios, gestao, perfis).AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana;;;;\nbruno@x.com;;Analista;;;\n"));

		relatorio.Value.Linhas.Single(l => l.Email == "ana@x.com").Status.Should().Be(StatusDaLinha.Erro);
		relatorio.Value.Linhas.Single(l => l.Email == "bruno@x.com").Status.Should().Be(StatusDaLinha.Criar);
		perfis.Perfis.Should().ContainSingle(p => p.UsuarioId == Bruno && p.Cargo == "Analista");
	}

	private static ImportarDiretorioHandler Criar(
		UsuariosParaDiretorioFalso usuarios, GestaoDeAcessoFalsa gestao, PerfisColaboradorFalso? perfis = null) =>
		new(usuarios, perfis ?? new PerfisColaboradorFalso(), new SetoresFalsos(), gestao, new TrilhaDeAcessoFalsa());

	[Fact]
	public async Task Importar_SecureGateForaDoAr_NaoInsisteLinhaALinha()
	{
		// Com a plataforma fora, cada chamada esperaria o timeout do HttpClient, em série: depois da
		// primeira indisponibilidade, as linhas seguintes com nome viram erro sem chamar de novo.
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com").Com(Carla, "carla@x.com");
		var gestao = new GestaoDeAcessoFalsa { FalharCom = IntranetErrors.Acesso.Indisponivel };
		var perfis = new PerfisColaboradorFalso();

		var relatorio = await Criar(usuarios, gestao, perfis).AplicarAsync(new ImportarDiretorioCommand(
			Cabecalho + "ana@x.com;Ana;;;;\nbruno@x.com;Bruno;;;;\ncarla@x.com;;Analista;;;\n"));

		gestao.Chamadas.Should().ContainSingle("só a primeira linha com nome chega à plataforma");
		relatorio.Value.Linhas.Where(l => l.Email != "carla@x.com").Should().OnlyContain(l => l.Status == StatusDaLinha.Erro);
		relatorio.Value.Linhas.Single(l => l.Email == "carla@x.com").Status.Should().Be(StatusDaLinha.Criar, "linha sem nome segue normal");
		relatorio.Value.Linhas.Single(l => l.Email == "bruno@x.com").Erro.Should().Contain("não foi possível gravar o nome");
	}
}
