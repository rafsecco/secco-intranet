using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Application.Salas.Agenda;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Salas;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Processo de espelhamento sobre o banco real (as corridas com a alteração e o cancelamento, o
/// isolamento entre tenants) e o estado do espelhamento na tela. Tudo numa única classe porque os
/// ciclos varrem os bancos compartilhados de teste: duas classes rodando ciclos ao mesmo tempo
/// tratariam as reservas uma da outra com agendas falsas diferentes. Cada teste cria só as próprias
/// reservas, confere só elas e as apaga no fim.
/// </summary>
public class EspelhamentoWorkerTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

	private static int _deslocamento = 100;

	private readonly List<(Guid Tenant, Guid Reserva, Guid Sala)> _criadas = [];
	private readonly List<(Guid Tenant, string EventoId)> _eventos = [];

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		factory.UsuariosDoDiretorio = new UsuariosParaDiretorioFalso().Com(Ana, "ana.teste@example.com", "Ana Teste");
	}

	public async Task DisposeAsync()
	{
		foreach (var (tenant, reserva, sala) in _criadas)
		{
			await using var escopo = NovoEscopo(factory.Services, tenant);
			var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

			await contexto.Reservas.Where(r => r.Id == reserva).ExecuteDeleteAsync();
			await contexto.Salas.Where(s => s.Id == sala).ExecuteDeleteAsync();
		}

		foreach (var (tenant, eventoId) in _eventos)
		{
			await using var escopo = NovoEscopo(factory.Services, tenant);
			var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

			await contexto.ExclusoesDeEventoPendentes.Where(e => e.EventoExternoId == eventoId).ExecuteDeleteAsync();
		}
	}

	private sealed class LigadoFixo(bool ligado) : IEspelhamentoLigado
	{
		public bool Ligado => ligado;
	}

	private sealed class LogDoTeste : ILogger<EspelhamentoWorker>
	{
		public List<(LogLevel Nivel, string Mensagem)> Registros { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Registros.Add((logLevel, formatter(state, exception)));
	}

	private static AsyncServiceScope NovoEscopo(IServiceProvider servicos, Guid tenant)
	{
		var escopo = servicos.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(tenant);

		return escopo;
	}

	/// <summary>Aplicação com a agenda falsa no lugar da externa; <paramref name="tenantsQueFalham"/> não conseguem nem resolvê-la.</summary>
	private WebApplicationFactory<Program> ComAgenda(AgendaFalsa agenda, bool ligado = true, params Guid[] tenantsQueFalham) =>
		factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
		{
			services.RemoveAll<IEspelhamentoLigado>();
			services.AddSingleton<IEspelhamentoLigado>(new LigadoFixo(ligado));
			services.AddScoped<ProcessarEspelhamentoHandler>();
			services.AddScoped<IAgendaExterna>(provider =>
				tenantsQueFalham.Contains(provider.GetRequiredService<ITenantContext>().TenantId ?? Guid.Empty)
					? throw new InvalidOperationException("Falha simulada do tenant.")
					: agenda);
		}));

	private static EspelhamentoWorker NovoWorker(IServiceProvider servicos, ILogger<EspelhamentoWorker>? log = null) =>
		new(
			servicos.GetRequiredService<IServiceScopeFactory>(),
			servicos.GetRequiredService<ITenantCatalog>(),
			new AgendaOptions { IntervaloEmSegundos = 30 },
			log ?? new LogDoTeste());

	/// <summary>Cria no tenant uma sala própria e uma reserva futura aguardando espelhamento.</summary>
	private async Task<Guid> CriarReservaAsync(Guid tenant, string titulo, bool falhou = false)
	{
		await using var escopo = NovoEscopo(factory.Services, tenant);
		var sala = new Sala($"Sala {Guid.NewGuid():N}"[..24], null, 8, null, "sala@exemplo.test", 1);
		await escopo.ServiceProvider.GetRequiredService<ISalaRepository>().AddAsync(sala);

		var inicio = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(Interlocked.Increment(ref _deslocamento)).AddHours(13), TimeSpan.Zero);
		var reserva = new Reserva(
			sala.Id, inicio, inicio.AddHours(1), titulo, "Pauta", Ana, "Ana Teste", "ana.teste@example.com", [], espelhar: true);

		if (falhou)
		{
			reserva.MarcarFalhaDefinitiva("Permissão negada na caixa.");
		}

		await escopo.ServiceProvider.GetRequiredService<IReservaRepository>().AddAsync(reserva);
		_criadas.Add((tenant, reserva.Id, sala.Id));

		return reserva.Id;
	}

	private async Task<Reserva?> LerAsync(Guid tenant, Guid id)
	{
		await using var escopo = NovoEscopo(factory.Services, tenant);

		return await escopo.ServiceProvider.GetRequiredService<IReservaRepository>().GetAsync(id);
	}

	private async Task<ExclusaoDeEventoPendente?> ExclusaoDoEventoAsync(Guid tenant, string eventoId)
	{
		await using var escopo = NovoEscopo(factory.Services, tenant);

		return await escopo.ServiceProvider.GetRequiredService<IntranetDbContext>().ExclusoesDeEventoPendentes
			.AsNoTracking()
			.SingleOrDefaultAsync(e => e.EventoExternoId == eventoId);
	}

	[Fact]
	public async Task Ciclo_PercorreOsDoisTenants()
	{
		var agenda = new AgendaFalsa();
		await using var app = ComAgenda(agenda);
		var alfa = await CriarReservaAsync(factory.TenantAlfa, "Ciclo Alfa");
		var beta = await CriarReservaAsync(factory.TenantBeta, "Ciclo Beta");

		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);

		(await LerAsync(factory.TenantAlfa, alfa))!.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		(await LerAsync(factory.TenantBeta, beta))!.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		agenda.Chamadas.Select(c => c.Evento?.ChaveDeIdempotencia).Should().Contain([alfa.ToString(), beta.ToString()]);
	}

	[Fact]
	public async Task FalhaNumTenant_NaoParaOOutro()
	{
		var agenda = new AgendaFalsa();
		await using var app = ComAgenda(agenda, true, factory.TenantAlfa);
		var alfa = await CriarReservaAsync(factory.TenantAlfa, "Falha Alfa");
		var beta = await CriarReservaAsync(factory.TenantBeta, "Falha Beta");
		var log = new LogDoTeste();

		await NovoWorker(app.Services, log).ExecutarUmCicloAsync(CancellationToken.None);

		(await LerAsync(factory.TenantAlfa, alfa))!.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		(await LerAsync(factory.TenantBeta, beta))!.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
		log.Registros.Should().Contain(r => r.Nivel == LogLevel.Error);
	}

	[Fact]
	public async Task AlteradaDuranteOEnvio_ContinuaAguardando_ERegistraOEvento()
	{
		var agenda = new AgendaFalsa();
		await using var app = ComAgenda(agenda);
		var id = await CriarReservaAsync(factory.TenantAlfa, "Corrida de alteração");
		var eventoId = $"evt-{Guid.NewGuid():N}";
		agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.Feito, eventoId));
		agenda.Durante = async operacao =>
		{
			if (operacao != "criar")
			{
				return;
			}

			// Outra pessoa altera a reserva enquanto o evento está sendo criado na agenda.
			await using var escopo = NovoEscopo(factory.Services, factory.TenantAlfa);
			var repositorio = escopo.ServiceProvider.GetRequiredService<IReservaRepository>();
			var reserva = (await repositorio.GetAsync(id))!;
			reserva.Alterar(reserva.SalaId, reserva.Inicio, reserva.Fim.AddMinutes(30), "Título alterado", null, [], espelhar: true);
			await repositorio.SaveChangesAsync();
		};

		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);

		var depois = (await LerAsync(factory.TenantAlfa, id))!;
		depois.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando, "a reserva mudou durante o envio");
		depois.Titulo.Should().Be("Título alterado");
		depois.Versao.Should().Be(2);
		depois.EventoExternoId.Should().Be(eventoId, "o evento criado precisa ficar registrado para o próximo ciclo atualizar, não duplicar");

		agenda.Durante = null;
		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);

		var chamada = agenda.Chamadas.Last();
		chamada.Operacao.Should().Be("atualizar");
		chamada.EventoId.Should().Be(eventoId);
		chamada.Evento!.Assunto.Should().Be("Título alterado");
		agenda.Chamadas.Count(c => c.Operacao == "criar" && c.Evento!.ChaveDeIdempotencia == id.ToString()).Should().Be(1);
		(await LerAsync(factory.TenantAlfa, id))!.Espelhamento.Should().Be(EstadoDoEspelhamento.Espelhada);
	}

	[Fact]
	public async Task AlteradaDuranteAAtualizacao_ContinuaAguardando()
	{
		var agenda = new AgendaFalsa();
		await using var app = ComAgenda(agenda);
		var id = await CriarReservaAsync(factory.TenantAlfa, "Corrida na atualização");
		var eventoId = $"evt-{Guid.NewGuid():N}";
		agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.Feito, eventoId));
		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);

		await using (var escopo = NovoEscopo(factory.Services, factory.TenantAlfa))
		{
			var repositorio = escopo.ServiceProvider.GetRequiredService<IReservaRepository>();
			var reserva = (await repositorio.GetAsync(id))!;
			reserva.Alterar(reserva.SalaId, reserva.Inicio, reserva.Fim, "Primeira mudança", null, [], espelhar: true);
			await repositorio.SaveChangesAsync();
		}

		agenda.Durante = async operacao =>
		{
			await using var escopo = NovoEscopo(factory.Services, factory.TenantAlfa);
			var repositorio = escopo.ServiceProvider.GetRequiredService<IReservaRepository>();
			var reserva = (await repositorio.GetAsync(id))!;
			reserva.Alterar(reserva.SalaId, reserva.Inicio, reserva.Fim, "Segunda mudança", null, [], espelhar: true);
			await repositorio.SaveChangesAsync();
		};

		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);

		var depois = (await LerAsync(factory.TenantAlfa, id))!;
		depois.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		depois.EventoExternoId.Should().Be(eventoId);
		depois.Titulo.Should().Be("Segunda mudança");
	}

	private async Task<(Guid Id, string EventoId)> CancelarDuranteACriacaoAsync(AgendaFalsa agenda)
	{
		var id = await CriarReservaAsync(factory.TenantAlfa, "Corrida de cancelamento");
		var eventoId = $"evt-{Guid.NewGuid():N}";
		_eventos.Add((factory.TenantAlfa, eventoId));
		agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.Feito, eventoId));
		agenda.Durante = async operacao =>
		{
			if (operacao != "criar")
			{
				return;
			}

			// A reserva é cancelada (ainda sem evento registrado) enquanto o evento está sendo criado.
			await using var escopo = NovoEscopo(factory.Services, factory.TenantAlfa);
			var cancelada = await escopo.ServiceProvider.GetRequiredService<CancelarReservaHandler>()
				.HandleAsync(id, new QuemPede(Guid.NewGuid(), true));
			cancelada.IsSuccess.Should().BeTrue();
		};

		return (id, eventoId);
	}

	[Fact]
	public async Task CanceladaDuranteOEnvio_OEventoCriadoViraExclusaoPendente_ESeApaga()
	{
		var agenda = new AgendaFalsa();
		await using var app = ComAgenda(agenda);
		var (id, eventoId) = await CancelarDuranteACriacaoAsync(agenda);

		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);

		(await LerAsync(factory.TenantAlfa, id)).Should().BeNull();
		agenda.Chamadas.Should().Contain(c => c.Operacao == "apagar" && c.EventoId == eventoId, "o evento criado não pode ficar órfão na agenda");
		(await ExclusaoDoEventoAsync(factory.TenantAlfa, eventoId)).Should().BeNull("apagado com sucesso, sai da fila");
	}

	[Fact]
	public async Task CanceladaDuranteOEnvio_ApagarFalha_AExclusaoFicaNaFila()
	{
		var agenda = new AgendaFalsa();
		await using var app = ComAgenda(agenda);
		var (id, eventoId) = await CancelarDuranteACriacaoAsync(agenda);
		agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaPassageira, Motivo: "Serviço fora do ar."));

		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);

		(await LerAsync(factory.TenantAlfa, id)).Should().BeNull();
		var pendencia = await ExclusaoDoEventoAsync(factory.TenantAlfa, eventoId);
		pendencia.Should().NotBeNull("o evento criado precisa ficar guardado até ser apagado");
		pendencia.Tentativas.Should().Be(1);
	}

	[Fact]
	public async Task FalhaPassageira_GravaEspera_ENaoTrataDeNovoNoMesmoCiclo()
	{
		var agenda = new AgendaFalsa();
		await using var app = ComAgenda(agenda);
		var id = await CriarReservaAsync(factory.TenantAlfa, "Falha passageira");
		agenda.Roteiro.Enqueue(new ResultadoDaAgenda(DesfechoDaAgenda.FalhaPassageira, Motivo: "Serviço fora do ar."));

		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);
		await NovoWorker(app.Services).ExecutarUmCicloAsync(CancellationToken.None);

		var depois = (await LerAsync(factory.TenantAlfa, id))!;
		depois.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		depois.TentativasDeEspelhamento.Should().Be(1);
		depois.ProximaTentativaEm.Should().NotBeNull();
		agenda.Chamadas.Count(c => c.Evento?.ChaveDeIdempotencia == id.ToString()).Should().Be(1);
	}

	private HttpClient CriarCliente(WebApplicationFactory<Program> app, Guid usuario, bool admin)
	{
		var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, admin ? "intranet-admin" : "diretorio-user");
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuario.ToString());

		return client;
	}

	[Fact]
	public async Task Detalhe_Falhou_AdminVeBotao_DonoVeSoOMotivo()
	{
		await using var app = ComAgenda(new AgendaFalsa());
		var id = await CriarReservaAsync(factory.TenantAlfa, "Reserva que falhou", falhou: true);
		var dono = CriarCliente(app, Ana, admin: false);
		var admin = CriarCliente(app, Guid.NewGuid(), admin: true);
		var outro = CriarCliente(app, Guid.NewGuid(), admin: false);

		var doDono = AuxiliaresDeHttp.Decodificar(await dono.GetStringAsync($"/salas/reservas/{id}"));
		var doAdmin = AuxiliaresDeHttp.Decodificar(await admin.GetStringAsync($"/salas/reservas/{id}"));
		var doOutro = AuxiliaresDeHttp.Decodificar(await outro.GetStringAsync($"/salas/reservas/{id}"));

		doDono.Should().Contain("Permissão negada na caixa.").And.NotContain($"/salas/reservas/{id}/espelhar");
		doAdmin.Should().Contain("Permissão negada na caixa.").And.Contain($"/salas/reservas/{id}/espelhar").And.Contain("Tentar de novo");
		doOutro.Should().NotContain("Permissão negada na caixa.").And.NotContain("/espelhar");
	}

	[Fact]
	public async Task TentarDeNovo_Admin_VoltaParaAFila_ComToken_ENaoAdmin404()
	{
		await using var app = ComAgenda(new AgendaFalsa());
		var id = await CriarReservaAsync(factory.TenantAlfa, "Reserva para tentar de novo", falhou: true);
		var dono = CriarCliente(app, Ana, admin: false);
		var admin = CriarCliente(app, Guid.NewGuid(), admin: true);

		var semToken = await admin.PostAsync($"/salas/reservas/{id}/espelhar", AuxiliaresDeHttp.Form());
		semToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);

		var tokenDono = await AuxiliaresDeHttp.TokenAsync(dono, $"/salas/reservas/{id}");
		var negado = await dono.PostAsync($"/salas/reservas/{id}/espelhar", AuxiliaresDeHttp.Form(("__RequestVerificationToken", tokenDono)));
		negado.StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await LerAsync(factory.TenantAlfa, id))!.Espelhamento.Should().Be(EstadoDoEspelhamento.Falhou);

		var token = await AuxiliaresDeHttp.TokenAsync(admin, $"/salas/reservas/{id}");
		var feito = await admin.PostAsync($"/salas/reservas/{id}/espelhar", AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));

		feito.EnsureSuccessStatusCode();
		var depois = (await LerAsync(factory.TenantAlfa, id))!;
		depois.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		depois.TentativasDeEspelhamento.Should().Be(0);
	}

	[Fact]
	public async Task Detalhe_EspelhamentoDesligado_NaoMostraEstado()
	{
		await using var app = ComAgenda(new AgendaFalsa(), ligado: false);
		var id = await CriarReservaAsync(factory.TenantAlfa, "Reserva sem espelhamento", falhou: true);
		var admin = CriarCliente(app, Guid.NewGuid(), admin: true);
		var dono = CriarCliente(app, Ana, admin: false);

		var doAdmin = AuxiliaresDeHttp.Decodificar(await admin.GetStringAsync($"/salas/reservas/{id}"));
		var doDono = AuxiliaresDeHttp.Decodificar(await dono.GetStringAsync($"/salas/reservas/{id}"));

		foreach (var html in new[] { doAdmin, doDono })
		{
			html.Should().Contain("Reserva sem espelhamento");
			html.Should().NotContain("Agenda externa").And.NotContain("Permissão negada na caixa.").And.NotContain("/espelhar");
		}
	}
}
