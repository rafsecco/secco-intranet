using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Vencimentos;
using Secco.Intranet.Domain.Vencimentos;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Varredura de avisos: decisão por item, registro antes do envio e compensação.</summary>
public class ProcessarAvisosDeVencimentoHandlerTests
{
	private const string Slug = "ti";

	// 2026-10-24 12:00Z = 09:00 em São Paulo.
	private static readonly DateOnly Hoje = new(2026, 10, 24);

	private sealed class DiretorioFalso(params UsuarioDoTenant[] usuarios) : IDiretorioDeUsuarios
	{
		public int Leituras { get; private set; }

		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(CancellationToken cancellationToken = default)
		{
			Leituras++;

			return Task.FromResult<IReadOnlyList<UsuarioDoTenant>>(usuarios);
		}
	}

	private sealed class NotificadorFalso : INotificadorDeMensagens
	{
		public List<MensagemParaEnviar> Enviadas { get; } = [];

		/// <summary>Falha uma única vez, na N-ésima chamada (1 = primeira); 0 nunca falha.</summary>
		public int FalharNaChamada { get; set; }

		/// <summary>Cancela (<see cref="OperationCanceledException"/>) na N-ésima chamada; 0 nunca cancela.</summary>
		public int CancelarNaChamada { get; set; }

		private int _chamadas;

		public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default)
		{
			_chamadas++;

			if (_chamadas == CancelarNaChamada)
			{
				throw new OperationCanceledException();
			}

			if (_chamadas == FalharNaChamada)
			{
				throw new NotificacaoIndisponivelException();
			}

			Enviadas.Add(mensagem);

			return Task.CompletedTask;
		}
	}

	private sealed class RepositorioFalso(params ItemParaAviso[] itens) : IItemVencimentoRepository
	{
		public List<AvisoDeVencimentoEnviado> Registrados { get; } = [];

		public List<Guid> Removidos { get; } = [];

		public List<CancellationToken> TokensDaRemocao { get; } = [];

		public bool Recusar { get; set; }

		public Task AddAsync(ItemVencimento item, CancellationToken ct = default) => throw new NotSupportedException();

		public Task<ItemVencimento?> GetParaEdicaoAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

		public Task<IReadOnlyList<ItemVencimento>> ListarDoSetorAsync(Guid setorId, bool incluirEncerrados, CancellationToken ct = default) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<RenovacaoDeVencimento>> ListarRenovacoesAsync(Guid itemId, CancellationToken ct = default) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<AvisoDeVencimentoEnviado>> ListarAvisosAsync(Guid itemId, CancellationToken ct = default) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<ItemParaAviso>> ListarAtivosParaAvisoAsync(CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyList<ItemParaAviso>>(itens);

		public Task<bool> TentarRegistrarAvisoAsync(AvisoDeVencimentoEnviado aviso, CancellationToken ct = default)
		{
			if (Recusar)
			{
				return Task.FromResult(false);
			}

			Registrados.Add(aviso);

			return Task.FromResult(true);
		}

		public Task RemoverAvisoAsync(Guid avisoId, CancellationToken ct = default)
		{
			Removidos.Add(avisoId);
			TokensDaRemocao.Add(ct);

			return Task.CompletedTask;
		}

		public Task AdicionarRenovacaoAsync(RenovacaoDeVencimento renovacao, CancellationToken ct = default) =>
			throw new NotSupportedException();

		public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
	}

	private sealed class LogFalso : ILogger<ProcessarAvisosDeVencimentoHandler>
	{
		public List<LogLevel> Niveis { get; } = [];

		public List<string> Mensagens { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Registrar(logLevel, formatter(state, exception));

		private void Registrar(LogLevel nivel, string mensagem)
		{
			Niveis.Add(nivel);
			Mensagens.Add(mensagem);
		}
	}

	private static UsuarioDoTenant Admin(string? email = "ana@empresa.com") => new(Guid.NewGuid(), email, ["ti-admin"]);

	/// <summary>
	/// Item que vence daqui a <paramref name="diasParaVencer"/> dias. Por padrão só tem o marco do
	/// próprio dia pedido, para o teste não esbarrar em marcos pulados; <paramref name="antecedencias"/>
	/// permite usar outros.
	/// </summary>
	private static ItemParaAviso Item(
		string titulo,
		int diasParaVencer,
		Guid? responsavel = null,
		IReadOnlyList<int>? antecedencias = null) =>
		new(
			new ItemVencimento(
				Guid.NewGuid(),
				titulo,
				null,
				Hoje.AddDays(diasParaVencer),
				responsavel,
				antecedencias ?? [Math.Max(diasParaVencer, 1)]),
			Slug,
			"Tecnologia",
			new HashSet<int>());

	private static ProcessarAvisosDeVencimentoHandler Montar(
		RepositorioFalso repositorio,
		IDiretorioDeUsuarios diretorio,
		NotificadorFalso notificador,
		ILogger<ProcessarAvisosDeVencimentoHandler>? log = null,
		string urlBase = "") =>
		new(
			repositorio,
			diretorio,
			notificador,
			new NotificacaoOptions { UrlBase = urlBase },
			new RelogioDeVencimentos(
				new FakeTimeProvider(DateTimeOffset.Parse("2026-10-24T12:00:00Z")), new VencimentosOptions()),
			log ?? NullLogger<ProcessarAvisosDeVencimentoHandler>.Instance);

	[Fact]
	public async Task EnviaUmLotePorCanal_ERegistraEnviado()
	{
		var comEmail = Admin("ana@empresa.com");
		var semEmail = Admin(null);
		var item = Item("Certificado A1", 7);
		var repositorio = new RepositorioFalso(item);
		var notificador = new NotificadorFalso();

		var enviados = await Montar(repositorio, new DiretorioFalso(comEmail, semEmail), notificador, urlBase: "https://intranet.exemplo.com/")
			.HandleAsync();

		enviados.Should().Be(1);

		var registro = repositorio.Registrados.Should().ContainSingle().Subject;
		registro.ItemVencimentoId.Should().Be(item.Item.Id);
		registro.Ciclo.Should().Be(1);
		registro.Marco.Should().Be(7);
		registro.Resultado.Should().Be(ResultadoDoAviso.Enviado);
		registro.QuantidadeDeDestinos.Should().Be(2);

		notificador.Enviadas.Should().HaveCount(2);
		var porEmail = notificador.Enviadas[0];
		porEmail.Canais.Should().Equal("in_app", "email");
		porEmail.Origem.Should().Be("vencimentos");
		porEmail.Tipo.Should().Be("aviso");
		porEmail.Titulo.Should().Be("Certificado A1 vence em 7 dias");
		porEmail.Link.Should().Be($"https://intranet.exemplo.com/ti/vencimentos/{item.Item.Id}");
		porEmail.Resumo.Should().Contain("Tecnologia").And.Contain("31 de outubro de 2026");
		porEmail.ProgramadaPara.Should().BeNull();
		porEmail.Destinos.Should().Equal(new DestinoDaMensagem(comEmail.Id, "ana@empresa.com"));
		notificador.Enviadas[1].Canais.Should().Equal("in_app");
		notificador.Enviadas[1].Destinos.Should().Equal(new DestinoDaMensagem(semEmail.Id, null));
	}

	[Fact]
	public async Task MarcoDistante_VaiSoParaOSino_ELinkRelativoSemUrlBase()
	{
		var admin = Admin();
		var item = Item("Contrato", 30);
		var notificador = new NotificadorFalso();

		await Montar(new RepositorioFalso(item), new DiretorioFalso(admin), notificador).HandleAsync();

		var mensagem = notificador.Enviadas.Should().ContainSingle().Subject;
		mensagem.Canais.Should().Equal("in_app");
		mensagem.Destinos.Should().Equal(new DestinoDaMensagem(admin.Id, null));
		mensagem.Titulo.Should().Be("Contrato vence em 30 dias");
		mensagem.Link.Should().Be($"/ti/vencimentos/{item.Item.Id}");
	}

	[Fact]
	public async Task Resumo_MostraResponsavelQuandoConhecido()
	{
		var responsavel = new UsuarioDoTenant(Guid.NewGuid(), "joao@empresa.com", ["ti-user"]);
		var item = Item("Licença", 1, responsavel.Id);
		var notificador = new NotificadorFalso();

		await Montar(new RepositorioFalso(item), new DiretorioFalso(Admin(), responsavel), notificador).HandleAsync();

		notificador.Enviadas.Should().OnlyContain(m => m.Resumo.Contains("Responsável: joao@empresa.com"));
		notificador.Enviadas.SelectMany(m => m.Destinos).Should().HaveCount(2);
	}

	[Fact]
	public async Task RegistroRecusado_NaoEnvia()
	{
		var repositorio = new RepositorioFalso(Item("Certificado", 7)) { Recusar = true };
		var notificador = new NotificadorFalso();

		var enviados = await Montar(repositorio, new DiretorioFalso(Admin()), notificador).HandleAsync();

		enviados.Should().Be(0);
		notificador.Enviadas.Should().BeEmpty();
		repositorio.Removidos.Should().BeEmpty();
	}

	[Fact]
	public async Task HubIndisponivel_DesfazORegistro_ESegueParaOProximoItem()
	{
		var primeiro = Item("Primeiro", 7);
		var segundo = Item("Segundo", 7);
		var repositorio = new RepositorioFalso(primeiro, segundo);
		var notificador = new NotificadorFalso { FalharNaChamada = 1 };

		var enviados = await Montar(repositorio, new DiretorioFalso(Admin()), notificador).HandleAsync();

		enviados.Should().Be(1);
		repositorio.Registrados.Should().HaveCount(2);

		var desfeito = repositorio.Registrados.Single(r => r.ItemVencimentoId == primeiro.Item.Id);
		repositorio.Removidos.Should().Equal(desfeito.Id);
		notificador.Enviadas.Should().ContainSingle().Which.Titulo.Should().Be("Segundo vence em 7 dias");
	}

	[Fact]
	public async Task SemDestinatario_RegistraELogaSemEnviar()
	{
		var item = Item("Certificado", 7);
		var repositorio = new RepositorioFalso(item);
		var notificador = new NotificadorFalso();
		var log = new LogFalso();
		var soUser = new UsuarioDoTenant(Guid.NewGuid(), "u@x.com", ["ti-user"]);

		var enviados = await Montar(repositorio, new DiretorioFalso(soUser), notificador, log).HandleAsync();

		enviados.Should().Be(0);
		notificador.Enviadas.Should().BeEmpty();

		var registro = repositorio.Registrados.Should().ContainSingle().Subject;
		registro.Marco.Should().Be(7);
		registro.Resultado.Should().Be(ResultadoDoAviso.SemDestinatario);
		registro.QuantidadeDeDestinos.Should().Be(0);
		log.Niveis.Should().Contain(LogLevel.Warning);
	}

	[Fact]
	public async Task DiretorioVazio_NaoRegistraNada_ParaEnviar()
	{
		var repositorio = new RepositorioFalso(Item("Certificado", 7));
		var notificador = new NotificadorFalso();
		var log = new LogFalso();

		var enviados = await Montar(repositorio, new DiretorioFalso(), notificador, log).HandleAsync();

		enviados.Should().Be(0);
		repositorio.Registrados.Should().BeEmpty();
		notificador.Enviadas.Should().BeEmpty();
		log.Niveis.Should().ContainSingle().Which.Should().Be(LogLevel.Warning);
	}

	[Fact]
	public async Task MarcosPulados_RegistradosSemEnvio()
	{
		// Cadastro tardio: a 5 dias do vencimento, 30 e 7 já passaram; só o 1 está adiante.
		// Com 30, 7 e 1 devidos hoje (item a 1 dia), o 1 é enviado e 30 e 7 são pulados.
		var item = Item("Certificado", 1, antecedencias: [30, 7, 1]);
		var repositorio = new RepositorioFalso(item);
		var notificador = new NotificadorFalso();

		var enviados = await Montar(repositorio, new DiretorioFalso(Admin()), notificador).HandleAsync();

		enviados.Should().Be(1);
		repositorio.Registrados
			.Where(r => r.Resultado == ResultadoDoAviso.Pulado)
			.Select(r => (r.Marco, r.QuantidadeDeDestinos))
			.Should().BeEquivalentTo([(30, 0), (7, 0)]);
		repositorio.Registrados.Single(r => r.Resultado == ResultadoDoAviso.Enviado).Marco.Should().Be(1);
		repositorio.Registrados.Should().OnlyContain(r => r.ItemVencimentoId == item.Item.Id && r.Ciclo == 1);
	}

	[Fact]
	public async Task MarcosPulados_RegistradosMesmoSemDestinatarios()
	{
		var item = Item("Certificado", 1, antecedencias: [30, 7, 1]);
		var repositorio = new RepositorioFalso(item);

		await Montar(repositorio, new DiretorioFalso(), new NotificadorFalso()).HandleAsync();

		repositorio.Registrados.Should().OnlyContain(r => r.Resultado == ResultadoDoAviso.Pulado);
		repositorio.Registrados.Select(r => r.Marco).Should().BeEquivalentTo([30, 7]);
	}

	[Fact]
	public async Task ListaDeUsuariosLidaUmaVezPorExecucao()
	{
		var diretorio = new DiretorioFalso(Admin());
		var repositorio = new RepositorioFalso(Item("A", 7), Item("B", 1), Item("C", 0, antecedencias: [1]));

		var enviados = await Montar(repositorio, diretorio, new NotificadorFalso()).HandleAsync();

		enviados.Should().Be(3);
		diretorio.Leituras.Should().Be(1);
	}

	[Fact]
	public async Task SemNadaParaEnviar_NaoLeOsUsuarios()
	{
		var diretorio = new DiretorioFalso(Admin());

		var enviados = await Montar(new RepositorioFalso(Item("Longe", 200, antecedencias: [30])), diretorio, new NotificadorFalso()).HandleAsync();

		enviados.Should().Be(0);
		diretorio.Leituras.Should().Be(0);
	}

	[Fact]
	public async Task FalhaNoSegundoLote_DesfazORegistroMesmoAssim()
	{
		// Falha no segundo lote: o registro é desfeito e o primeiro lote será reenviado
		// na próxima execução (custo aceito).
		var item = Item("Certificado", 7);
		var repositorio = new RepositorioFalso(item);
		var notificador = new NotificadorFalso { FalharNaChamada = 2 };

		var enviados = await Montar(repositorio, new DiretorioFalso(Admin("a@x.com"), Admin(null)), notificador).HandleAsync();

		enviados.Should().Be(0);
		notificador.Enviadas.Should().ContainSingle();
		repositorio.Removidos.Should().Equal(repositorio.Registrados.Single().Id);
	}

	[Fact]
	public async Task CancelamentoDuranteOEnvio_DesfazORegistro_ERelanca()
	{
		var item = Item("Certificado", 7);
		var repositorio = new RepositorioFalso(item);
		var notificador = new NotificadorFalso { CancelarNaChamada = 1 };
		using var cts = new CancellationTokenSource();

		var acao = () => Montar(repositorio, new DiretorioFalso(Admin()), notificador).HandleAsync(cts.Token);

		await acao.Should().ThrowAsync<OperationCanceledException>();
		repositorio.Removidos.Should().Equal(repositorio.Registrados.Single().Id);
		repositorio.TokensDaRemocao.Should().ContainSingle().Which.Should().Be(CancellationToken.None);
	}

	[Fact]
	public async Task Logs_CitamOIdDoItem_NuncaOTitulo()
	{
		var diretorioVazio = Item("Segredo A", 7);
		var logVazio = new LogFalso();
		await Montar(new RepositorioFalso(diretorioVazio), new DiretorioFalso(), new NotificadorFalso(), logVazio).HandleAsync();

		var semDestinatario = Item("Segredo B", 7);
		var logSemDestino = new LogFalso();
		await Montar(
			new RepositorioFalso(semDestinatario),
			new DiretorioFalso(new UsuarioDoTenant(Guid.NewGuid(), "u@x.com", ["ti-user"])),
			new NotificadorFalso(),
			logSemDestino).HandleAsync();

		var indisponivel = Item("Segredo C", 7);
		var logIndisponivel = new LogFalso();
		await Montar(
			new RepositorioFalso(indisponivel),
			new DiretorioFalso(Admin()),
			new NotificadorFalso { FalharNaChamada = 1 },
			logIndisponivel).HandleAsync();

		logVazio.Mensagens.Should().ContainSingle().Which.Should().Contain(diretorioVazio.Item.Id.ToString()).And.NotContain("Segredo");
		logSemDestino.Mensagens.Should().ContainSingle().Which.Should().Contain(semDestinatario.Item.Id.ToString()).And.NotContain("Segredo");
		logIndisponivel.Mensagens.Should().ContainSingle().Which.Should().Contain(indisponivel.Item.Id.ToString()).And.NotContain("Segredo");
	}
}
