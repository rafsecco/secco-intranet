using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Reservar, alterar e cancelar com os handlers de verdade sobre o repositório real e o banco do
/// tenant: prova que os instantes em UTC chegam ao banco e que o conflito de horário não grava.
/// </summary>
public class ReservaFluxoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private readonly Guid _anaId = Guid.NewGuid();
	private readonly Guid _betoId = Guid.NewGuid();
	private readonly UsuariosParaDiretorioFalso _diretorio = new();

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		_diretorio
			.Com(_anaId, "ana@exemplo.test", "Ana Souza")
			.Com(_betoId, "beto@exemplo.test", "Beto Lima");
		factory.UsuariosDoDiretorio = _diretorio;
	}

	public Task DisposeAsync()
	{
		factory.UsuariosDoDiretorio = null;

		return Task.CompletedTask;
	}

	private AsyncServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	private async Task<Sala> CriarSalaAsync()
	{
		await using var escopo = NovoEscopo();
		var sala = new Sala($"Sala {Guid.NewGuid():N}"[..24], null, 8, null, null, 1);
		await escopo.ServiceProvider.GetRequiredService<ISalaRepository>().AddAsync(sala);

		return sala;
	}

	private static DateOnly DiaFuturo(int aDiante) =>
		DateOnly.FromDateTime(DateTime.UtcNow.AddDays(aDiante));

	private ReservarSalaCommand Pedido(Sala sala, DateOnly dia, int inicioH, int fimH, Guid? quem = null, IReadOnlyList<Guid>? convidados = null) =>
		new(sala.Id, dia, new TimeOnly(inicioH, 0), new TimeOnly(fimH, 0), "Alinhamento semanal", "Pauta", convidados ?? [], new QuemPede(quem ?? _anaId, false));

	private async Task<T> ComHandlerAsync<T>(Func<IServiceProvider, Task<T>> acao)
	{
		await using var escopo = NovoEscopo();

		return await acao(escopo.ServiceProvider);
	}

	[Fact]
	public async Task Reservar_Alterar_Cancelar_NoBanco()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaFuturo(30);

		var criada = await ComHandlerAsync(sp => sp.GetRequiredService<ReservarSalaHandler>()
			.HandleAsync(Pedido(sala, dia, 10, 11, convidados: [_betoId])));

		criada.IsSuccess.Should().BeTrue();

		await using (var leitura = NovoEscopo())
		{
			var lida = await leitura.ServiceProvider.GetRequiredService<IReservaRepository>().GetAsync(criada.Value);

			lida.Should().NotBeNull();
			lida.Inicio.Offset.Should().Be(TimeSpan.Zero);
			lida.ReservadoPorNome.Should().Be("Ana Souza");
			lida.Convidados.Should().ContainSingle().Which.Nome.Should().Be("Beto Lima");
		}

		var alterada = await ComHandlerAsync(sp => sp.GetRequiredService<AlterarReservaHandler>().HandleAsync(
			new AlterarReservaCommand(
				criada.Value, sala.Id, dia, new TimeOnly(14, 0), new TimeOnly(15, 0), "Novo título", null, [], new QuemPede(_anaId, false))));

		alterada.IsSuccess.Should().BeTrue();

		var dto = await ComHandlerAsync(sp => sp.GetRequiredService<ObterReservaHandler>()
			.HandleAsync(criada.Value, new QuemPede(_anaId, false)));

		dto.Value.Titulo.Should().Be("Novo título");
		dto.Value.Inicio.Should().Be(new TimeOnly(14, 0));
		dto.Value.Dia.Should().Be(dia);
		dto.Value.Convidados.Should().BeEmpty();
		dto.Value.PodeAlterar.Should().BeTrue();

		var cancelada = await ComHandlerAsync(sp => sp.GetRequiredService<CancelarReservaHandler>()
			.HandleAsync(criada.Value, new QuemPede(_anaId, false)));

		cancelada.IsSuccess.Should().BeTrue();

		await using var depois = NovoEscopo();
		(await depois.ServiceProvider.GetRequiredService<IReservaRepository>().GetAsync(criada.Value)).Should().BeNull();
	}

	[Fact]
	public async Task Reservar_Conflito_NaoGrava()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaFuturo(31);

		var primeira = await ComHandlerAsync(sp => sp.GetRequiredService<ReservarSalaHandler>().HandleAsync(Pedido(sala, dia, 10, 12)));
		var segunda = await ComHandlerAsync(sp => sp.GetRequiredService<ReservarSalaHandler>().HandleAsync(Pedido(sala, dia, 11, 13, _betoId)));
		var encostada = await ComHandlerAsync(sp => sp.GetRequiredService<ReservarSalaHandler>().HandleAsync(Pedido(sala, dia, 12, 13, _betoId)));

		primeira.IsSuccess.Should().BeTrue();
		segunda.IsFailure.Should().BeTrue();
		segunda.Error.Description.Should().Be("Sala ocupada das 10:00 às 12:00 por Ana Souza.");
		encostada.IsSuccess.Should().BeTrue();

		var ocupacao = await ComHandlerAsync(sp => sp.GetRequiredService<ListarOcupacaoDoDiaHandler>()
			.HandleAsync(dia, new QuemPede(_anaId, false)));

		ocupacao.Reservas.Where(r => r.SalaId == sala.Id).Should().HaveCount(2);
	}

	[Fact]
	public async Task Cancelar_ComEventoExterno_DeixaExclusaoPendenteNoBanco()
	{
		var sala = await CriarSalaAsync();
		var criada = await ComHandlerAsync(sp => sp.GetRequiredService<ReservarSalaHandler>()
			.HandleAsync(Pedido(sala, DiaFuturo(32), 10, 11)));

		await using (var escopo = NovoEscopo())
		{
			var repositorio = escopo.ServiceProvider.GetRequiredService<IReservaRepository>();
			var reserva = await repositorio.GetAsync(criada.Value);
			reserva!.MarcarEspelhada($"evento-{criada.Value:N}");
			await repositorio.SaveChangesAsync();
		}

		var cancelada = await ComHandlerAsync(sp => sp.GetRequiredService<CancelarReservaHandler>()
			.HandleAsync(criada.Value, new QuemPede(_anaId, false)));

		cancelada.IsSuccess.Should().BeTrue();

		await using var leitura = NovoEscopo();
		var pendentes = await leitura.ServiceProvider.GetRequiredService<IReservaRepository>()
			.ListarExclusoesPendentesAsync(DateTimeOffset.UtcNow.AddYears(1), 1000);

		pendentes.Should().Contain(p => p.EventoExternoId == $"evento-{criada.Value:N}");
	}
}
