using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Infrastructure;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.Intranet.Infrastructure.Repositories;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A trava da sala nos dois engines (ADR-0018): gravações simultâneas da mesma sala se serializam
/// e o conflito de horário é detectado; salas diferentes não se atrapalham.
/// </summary>
public class ReservaConcorrenciaTests(BancosDeMigracaoFixture bancos) : IClassFixture<BancosDeMigracaoFixture>
{
	private const int Tarefas = 8;

	private static readonly DateTimeOffset Dez = new(2027, 3, 16, 10, 0, 0, TimeSpan.Zero);

	public static TheoryData<IntranetDatabaseProvider> Engines => new()
	{
		IntranetDatabaseProvider.SqlServer,
		IntranetDatabaseProvider.PostgreSql,
	};

	/// <summary>Banco migrado com as salas pedidas; devolve a fábrica de contextos e os ids das salas.</summary>
	private async Task<(Func<IntranetDbContext> Contexto, IReadOnlyList<Guid> Salas)> PrepararAsync(
		IntranetDatabaseProvider provider, int salas)
	{
		var fabrica = await bancos.CriarFabricaDeContextosAsync(provider);

		await using var contexto = fabrica();
		await contexto.Database.MigrateAsync();

		var criadas = Enumerable.Range(1, salas)
			.Select(n => new Sala($"Sala {n}", null, null, null, null, n))
			.ToList();
		contexto.Salas.AddRange(criadas);
		await contexto.SaveChangesAsync();

		return (fabrica, criadas.Select(sala => sala.Id).ToList());
	}

	private static Reserva NovaReserva(Guid salaId, DateTimeOffset inicio, TimeSpan duracao) =>
		new(salaId, inicio, inicio + duracao, "Reunião", null, Guid.NewGuid(), "Maria Teste", null, [], espelhar: false);

	/// <summary>Tenta reservar no mesmo molde da aplicação: confere o conflito e grava, tudo com a sala travada.</summary>
	private static async Task<bool> TentarReservarAsync(
		Func<IntranetDbContext> fabrica, Guid salaId, DateTimeOffset inicio, TimeSpan duracao)
	{
		await using var contexto = fabrica();
		var repositorio = new ReservaRepository(contexto);

		return await repositorio.ComSalaTravadaAsync(salaId, async ct =>
		{
			var fim = inicio + duracao;

			if (await repositorio.PrimeiraSobrepostaAsync(salaId, inicio, fim, null, ct) is not null)
			{
				return false;
			}

			await repositorio.AddAsync(NovaReserva(salaId, inicio, duracao), ct);

			return true;
		});
	}

	private static async Task<int> ContarAsync(Func<IntranetDbContext> fabrica, Guid? salaId = null)
	{
		await using var contexto = fabrica();

		return await contexto.Reservas.CountAsync(reserva => salaId == null || reserva.SalaId == salaId);
	}

	[Theory]
	[MemberData(nameof(Engines))]
	public async Task MesmaSala_GravacoesSimultaneas_SoUmaEntra(IntranetDatabaseProvider provider)
	{
		var (fabrica, salas) = await PrepararAsync(provider, 1);

		var resultados = await Task.WhenAll(Enumerable.Range(0, Tarefas)
			.Select(_ => Task.Run(() => TentarReservarAsync(fabrica, salas[0], Dez, TimeSpan.FromHours(1)))));

		resultados.Count(entrou => entrou).Should().Be(1);
		(await ContarAsync(fabrica)).Should().Be(1);
	}

	[Theory]
	[MemberData(nameof(Engines))]
	public async Task SalasDiferentes_GravacoesSimultaneas_TodasEntram(IntranetDatabaseProvider provider)
	{
		var (fabrica, salas) = await PrepararAsync(provider, Tarefas);

		var resultados = await Task.WhenAll(salas
			.Select(sala => Task.Run(() => TentarReservarAsync(fabrica, sala, Dez, TimeSpan.FromHours(1)))));

		resultados.Should().OnlyContain(entrou => entrou);
		(await ContarAsync(fabrica)).Should().Be(Tarefas);
	}

	[Theory]
	[MemberData(nameof(Engines))]
	public async Task AcaoQueLanca_NadaFicaGravado(IntranetDatabaseProvider provider)
	{
		var (fabrica, salas) = await PrepararAsync(provider, 1);

		await using (var contexto = fabrica())
		{
			var repositorio = new ReservaRepository(contexto);

			var tentar = async () => await repositorio.ComSalaTravadaAsync<bool>(salas[0], async ct =>
			{
				await repositorio.AddAsync(NovaReserva(salas[0], Dez, TimeSpan.FromHours(1)), ct);

				throw new InvalidOperationException("falha depois de gravar");
			});

			await tentar.Should().ThrowAsync<InvalidOperationException>().WithMessage("falha depois de gravar");
		}

		(await ContarAsync(fabrica)).Should().Be(0);

		// A trava foi solta com o rollback: a mesma sala aceita a reserva logo em seguida.
		(await TentarReservarAsync(fabrica, salas[0], Dez, TimeSpan.FromHours(1))).Should().BeTrue();
	}

	[Theory]
	[MemberData(nameof(Engines))]
	public async Task Encostadas_AsDuasEntram(IntranetDatabaseProvider provider)
	{
		var (fabrica, salas) = await PrepararAsync(provider, 1);

		var resultados = await Task.WhenAll(
			Task.Run(() => TentarReservarAsync(fabrica, salas[0], Dez, TimeSpan.FromHours(1))),
			Task.Run(() => TentarReservarAsync(fabrica, salas[0], Dez.AddHours(1), TimeSpan.FromHours(1))));

		resultados.Should().OnlyContain(entrou => entrou);
		(await ContarAsync(fabrica, salas[0])).Should().Be(2);
	}
}
