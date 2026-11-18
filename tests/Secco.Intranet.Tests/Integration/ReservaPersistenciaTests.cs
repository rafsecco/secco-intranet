using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Persistência de salas e reservas contra o banco de verdade: convidados que vão e vêm com a
/// reserva, consulta da agenda da pessoa e a seleção do que o espelhamento precisa tentar.
/// </summary>
public class ReservaPersistenciaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly DateTimeOffset Agora = new(2027, 3, 15, 12, 0, 0, TimeSpan.Zero);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private AsyncServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	private async Task<Sala> CriarSalaAsync()
	{
		await using var escopo = NovoEscopo();
		var sala = new Sala($"Sala {Guid.NewGuid():N}"[..24], "Andar 2", 8, "TV", null, 1);
		await escopo.ServiceProvider.GetRequiredService<ISalaRepository>().AddAsync(sala);

		return sala;
	}

	private static Reserva NovaReserva(
		Guid salaId,
		DateTimeOffset inicio,
		Guid? donoId = null,
		IEnumerable<ConvidadoDaReserva>? convidados = null,
		bool espelhar = false) =>
		new(
			salaId,
			inicio,
			inicio.AddHours(1),
			"Alinhamento semanal",
			"Pauta do trimestre",
			donoId ?? Guid.NewGuid(),
			"Maria Teste",
			"maria.teste@exemplo.test",
			convidados ?? [],
			espelhar);

	private async Task GravarAsync(Reserva reserva)
	{
		await using var escopo = NovoEscopo();
		await escopo.ServiceProvider.GetRequiredService<IReservaRepository>().AddAsync(reserva);
	}

	[Fact]
	public async Task Salvar_ReservaComConvidados_LeDeVolta()
	{
		var sala = await CriarSalaAsync();
		var convidadoA = new ConvidadoDaReserva(Guid.NewGuid(), "Ana Teste", "ana.teste@exemplo.test");
		var convidadoB = new ConvidadoDaReserva(Guid.NewGuid(), "Beto Teste", null);
		var reserva = NovaReserva(sala.Id, Agora.AddDays(1), convidados: [convidadoA, convidadoB], espelhar: true);

		await GravarAsync(reserva);

		await using var leitura = NovoEscopo();
		var lida = await leitura.ServiceProvider.GetRequiredService<IReservaRepository>().GetAsync(reserva.Id);

		lida.Should().NotBeNull();
		lida.SalaId.Should().Be(sala.Id);
		lida.Inicio.Should().Be(reserva.Inicio);
		lida.Fim.Should().Be(reserva.Fim);
		lida.Titulo.Should().Be("Alinhamento semanal");
		lida.Descricao.Should().Be("Pauta do trimestre");
		lida.ReservadoPorId.Should().Be(reserva.ReservadoPorId);
		lida.ReservadoPorNome.Should().Be("Maria Teste");
		lida.Versao.Should().Be(1);
		lida.Espelhamento.Should().Be(EstadoDoEspelhamento.Aguardando);
		lida.Convidados.Should().HaveCount(2);
		lida.Convidados.Select(c => c.UsuarioId).Should().BeEquivalentTo([convidadoA.UsuarioId, convidadoB.UsuarioId]);
		lida.Convidados.Single(c => c.UsuarioId == convidadoA.UsuarioId).Email.Should().Be("ana.teste@exemplo.test");
		lida.Convidados.Should().OnlyContain(c => c.ReservaId == reserva.Id);
	}

	[Fact]
	public async Task Alterar_TrocandoConvidados_ApagaOsQueSairam()
	{
		var sala = await CriarSalaAsync();
		var sai = new ConvidadoDaReserva(Guid.NewGuid(), "Ana Teste", null);
		var fica = new ConvidadoDaReserva(Guid.NewGuid(), "Beto Teste", null);
		var reserva = NovaReserva(sala.Id, Agora.AddDays(1), convidados: [sai, fica]);
		await GravarAsync(reserva);

		await using (var edicao = NovoEscopo())
		{
			var repositorio = edicao.ServiceProvider.GetRequiredService<IReservaRepository>();
			var carregada = (await repositorio.GetAsync(reserva.Id))!;
			var novo = new ConvidadoDaReserva(Guid.NewGuid(), "Caio Teste", null);

			carregada.Alterar(
				sala.Id, carregada.Inicio, carregada.Fim, "Novo título", null,
				[new ConvidadoDaReserva(fica.UsuarioId, "Beto Teste", null), novo], espelhar: false);
			await repositorio.SaveChangesAsync();
		}

		await using var leitura = NovoEscopo();
		var lida = (await leitura.ServiceProvider.GetRequiredService<IReservaRepository>().GetAsync(reserva.Id))!;

		lida.Titulo.Should().Be("Novo título");
		lida.Versao.Should().Be(2);
		lida.Convidados.Should().HaveCount(2);
		lida.Convidados.Select(c => c.UsuarioId).Should().NotContain(sai.UsuarioId);

		var contexto = leitura.ServiceProvider.GetRequiredService<IntranetDbContext>();
		(await contexto.ConvidadosDasReservas.CountAsync(c => c.ReservaId == reserva.Id)).Should().Be(2);
	}

	[Fact]
	public async Task ExcluirReserva_LevaOsConvidados()
	{
		var sala = await CriarSalaAsync();
		var convidado = new ConvidadoDaReserva(Guid.NewGuid(), "Ana Teste", null);
		var reserva = NovaReserva(sala.Id, Agora.AddDays(1), convidados: [convidado]);
		await GravarAsync(reserva);

		await using (var exclusao = NovoEscopo())
		{
			var repositorio = exclusao.ServiceProvider.GetRequiredService<IReservaRepository>();
			var carregada = (await repositorio.GetAsync(reserva.Id))!;
			repositorio.Remover(carregada);
			await repositorio.SaveChangesAsync();
		}

		await using var leitura = NovoEscopo();
		(await leitura.ServiceProvider.GetRequiredService<IReservaRepository>().GetAsync(reserva.Id)).Should().BeNull();

		var contexto = leitura.ServiceProvider.GetRequiredService<IntranetDbContext>();
		(await contexto.ConvidadosDasReservas.CountAsync(c => c.ReservaId == reserva.Id)).Should().Be(0);
	}

	[Fact]
	public async Task ListarFuturasDaPessoa_TrazDonoEConvidado_SemPassadas()
	{
		var sala = await CriarSalaAsync();
		var pessoa = Guid.NewGuid();
		var outra = Guid.NewGuid();

		var comoDona = NovaReserva(sala.Id, Agora.AddDays(1), donoId: pessoa);
		var comoConvidada = NovaReserva(
			sala.Id, Agora.AddDays(2), convidados: [new ConvidadoDaReserva(pessoa, "Maria Teste", null)]);
		var emAndamento = NovaReserva(sala.Id, Agora.AddMinutes(-30), donoId: pessoa);
		var passada = NovaReserva(sala.Id, Agora.AddDays(-1), donoId: pessoa);
		var deOutra = NovaReserva(
			sala.Id, Agora.AddDays(3), donoId: outra, convidados: [new ConvidadoDaReserva(Guid.NewGuid(), "Zeca Teste", null)]);

		foreach (var reserva in new[] { comoDona, comoConvidada, emAndamento, passada, deOutra })
		{
			await GravarAsync(reserva);
		}

		await using var leitura = NovoEscopo();
		var lista = await leitura.ServiceProvider.GetRequiredService<IReservaRepository>()
			.ListarFuturasDaPessoaAsync(pessoa, Agora);

		lista.Select(r => r.Id).Should().Equal(emAndamento.Id, comoDona.Id, comoConvidada.Id);
		lista.Single(r => r.Id == comoConvidada.Id).Convidados.Should().ContainSingle();
	}

	[Fact]
	public async Task ListarParaEspelhar_SoAguardandoComEsperaVencida()
	{
		var sala = await CriarSalaAsync();

		var nova = NovaReserva(sala.Id, Agora.AddDays(1), espelhar: true);
		var comEsperaVencida = NovaReserva(sala.Id, Agora.AddDays(2), espelhar: true);
		comEsperaVencida.RegistrarFalhaPassageira("indisponível", Agora.AddHours(-2));
		var comEsperaPendente = NovaReserva(sala.Id, Agora.AddDays(3), espelhar: true);
		comEsperaPendente.RegistrarFalhaPassageira("indisponível", Agora);
		var semEspelho = NovaReserva(sala.Id, Agora.AddDays(4));
		var espelhada = NovaReserva(sala.Id, Agora.AddDays(5), espelhar: true);
		espelhada.MarcarEspelhada("evento-1");
		var falhou = NovaReserva(sala.Id, Agora.AddDays(6), espelhar: true);
		falhou.MarcarFalhaDefinitiva("sem permissão");

		foreach (var reserva in new[] { nova, comEsperaVencida, comEsperaPendente, semEspelho, espelhada, falhou })
		{
			await GravarAsync(reserva);
		}

		await using var leitura = NovoEscopo();
		var lista = await leitura.ServiceProvider.GetRequiredService<IReservaRepository>()
			.ListarParaEspelharAsync(Agora, 1000);

		lista.Where(r => r.SalaId == sala.Id).Select(r => r.Id)
			.Should().BeEquivalentTo([nova.Id, comEsperaVencida.Id]);
	}

	[Fact]
	public async Task PrimeiraSobreposta_IgnoraEncostadasEAReservaIgnorada()
	{
		var sala = await CriarSalaAsync();
		var dez = new DateTimeOffset(2027, 3, 16, 10, 0, 0, TimeSpan.Zero);
		var existente = NovaReserva(sala.Id, dez);
		await GravarAsync(existente);

		await using var escopo = NovoEscopo();
		var repositorio = escopo.ServiceProvider.GetRequiredService<IReservaRepository>();

		(await repositorio.PrimeiraSobrepostaAsync(sala.Id, dez.AddMinutes(30), dez.AddMinutes(90), null))
			.Should().NotBeNull();
		(await repositorio.PrimeiraSobrepostaAsync(sala.Id, dez.AddHours(1), dez.AddHours(2), null))
			.Should().BeNull("extremos que apenas se tocam não conflitam");
		(await repositorio.PrimeiraSobrepostaAsync(sala.Id, dez.AddHours(-1), dez, null))
			.Should().BeNull();
		(await repositorio.PrimeiraSobrepostaAsync(sala.Id, dez, dez.AddHours(1), existente.Id))
			.Should().BeNull("a própria reserva é ignorada ao editar");
	}

	[Fact]
	public async Task Sala_NomeEmUso_ReservaFutura_ERemocaoBarradaPorReserva()
	{
		var sala = await CriarSalaAsync();
		await GravarAsync(NovaReserva(sala.Id, Agora.AddDays(1)));

		await using var escopo = NovoEscopo();
		var salas = escopo.ServiceProvider.GetRequiredService<ISalaRepository>();

		(await salas.NomeEmUsoAsync(sala.Nome, null)).Should().BeTrue();
		(await salas.NomeEmUsoAsync(sala.Nome, sala.Id)).Should().BeFalse();
		(await salas.NomeEmUsoAsync("Nome que ninguém usa " + Guid.NewGuid(), null)).Should().BeFalse();
		(await salas.TemReservaFuturaAsync(sala.Id, Agora)).Should().BeTrue();
		(await salas.TemReservaFuturaAsync(sala.Id, Agora.AddDays(30))).Should().BeFalse();

		var carregada = (await salas.GetAsync(sala.Id))!;
		salas.Remover(carregada);
		var remover = async () => await salas.SaveChangesAsync();
		await remover.Should().ThrowAsync<DbUpdateException>("a sala com reservas não pode ser apagada");
	}
}
