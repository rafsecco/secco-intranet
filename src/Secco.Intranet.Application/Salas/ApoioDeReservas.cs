using System.Text.Json;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Horário, título e descrição já validados; os instantes estão em UTC.</summary>
internal sealed record DadosDaReserva(DateTimeOffset Inicio, DateTimeOffset Fim, string Titulo, string? Descricao);

/// <summary>Validação, nomes e mapeamento compartilhados pelos handlers de reserva.</summary>
internal static class ApoioDeReservas
{
	/// <summary>Nome exibido para quem pede e não consta entre os ativos do Diretório (modo aberto de desenvolvimento).</summary>
	public const string NomeDoVisitante = "Visitante";

	/// <summary>Valida título, descrição e horário contra o relógio; devolve os instantes em UTC.</summary>
	public static Result<DadosDaReserva> Validar(
		RelogioDeSalas relogio,
		DateOnly dia,
		TimeOnly horaInicio,
		TimeOnly horaFim,
		string? titulo,
		string? descricao)
	{
		var tituloAparado = titulo?.Trim();

		if (string.IsNullOrEmpty(tituloAparado))
		{
			return IntranetErrors.Salas.TituloRequired;
		}

		if (tituloAparado.Length > Reserva.TituloMaximo)
		{
			return IntranetErrors.Salas.TituloTooLong(Reserva.TituloMaximo);
		}

		var descricaoAparada = descricao?.Trim();
		descricaoAparada = string.IsNullOrEmpty(descricaoAparada) ? null : descricaoAparada;

		if (descricaoAparada is { Length: > Reserva.DescricaoMaxima })
		{
			return IntranetErrors.Salas.DescricaoTooLong(Reserva.DescricaoMaxima);
		}

		if (horaFim <= horaInicio || !NoPasso(horaInicio) || !NoPasso(horaFim))
		{
			return IntranetErrors.Salas.HorarioInvalido;
		}

		var inicio = relogio.ParaUtc(dia, horaInicio);
		var fim = relogio.ParaUtc(dia, horaFim);

		if (inicio is null || fim is null)
		{
			return IntranetErrors.Salas.HorarioInvalido;
		}

		if (inicio.Value <= relogio.Agora())
		{
			return IntranetErrors.Salas.NoPassado;
		}

		return new DadosDaReserva(inicio.Value, fim.Value, tituloAparado, descricaoAparada);
	}

	/// <summary>Tira repetidos e o dono da lista: o domínio recusaria os repetidos e ignoraria o dono.</summary>
	public static List<Guid> NormalizarConvidados(IReadOnlyList<Guid>? convidados, Guid donoId) =>
		[.. (convidados ?? []).Where(id => id != donoId).Distinct()];

	/// <summary>Ativos do Diretório por id; falha se a consulta falhar.</summary>
	public static async Task<Result<IReadOnlyDictionary<Guid, UsuarioParaDiretorio>>> CarregarAtivosAsync(
		IUsuariosParaDiretorio diretorio, CancellationToken ct)
	{
		var ativos = await diretorio.ListarAtivosAsync(ct).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return IntranetErrors.Salas.DiretorioIndisponivel;
		}

		IReadOnlyDictionary<Guid, UsuarioParaDiretorio> porId = ativos.Value
			.GroupBy(usuario => usuario.Id)
			.ToDictionary(grupo => grupo.Key, grupo => grupo.First());

		return Result.Success(porId);
	}

	/// <summary>Nome de uma pessoa: o do Diretório ou, sem ele, o e-mail.</summary>
	public static string Nome(UsuarioParaDiretorio usuario) =>
		string.IsNullOrWhiteSpace(usuario.Nome) ? usuario.Email : usuario.Nome;

	/// <summary>Monta os convidados a gravar; devolve <c>null</c> quando algum não é usuário ativo.</summary>
	public static List<ConvidadoDaReserva>? MontarConvidados(
		IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, UsuarioParaDiretorio> ativos)
	{
		var convidados = new List<ConvidadoDaReserva>(ids.Count);

		foreach (var id in ids)
		{
			if (!ativos.TryGetValue(id, out var usuario))
			{
				return null;
			}

			convidados.Add(new ConvidadoDaReserva(usuario.Id, Nome(usuario), usuario.Email));
		}

		return convidados;
	}

	/// <summary>Converte a reserva, com horários locais e a permissão de quem pede.</summary>
	public static ReservaDto Mapear(Reserva reserva, string salaNome, RelogioDeSalas relogio, QuemPede quem)
	{
		var (dia, inicio) = relogio.ParaLocal(reserva.Inicio);
		var (_, fim) = relogio.ParaLocal(reserva.Fim);
		var podeAlterar = (reserva.ReservadoPorId == quem.UsuarioId || quem.EhAdministrador)
			&& !reserva.JaComecou(relogio.Agora());

		return new ReservaDto(
			reserva.Id,
			reserva.SalaId,
			salaNome,
			dia,
			inicio,
			fim,
			reserva.Titulo,
			reserva.Descricao,
			new PessoaDaReservaDto(reserva.ReservadoPorId, reserva.ReservadoPorNome),
			[.. reserva.Convidados.Select(c => new PessoaDaReservaDto(c.UsuarioId, c.Nome))],
			podeAlterar,
			reserva.Espelhamento,
			reserva.MotivoDaFalha);
	}

	/// <summary>Erro de conflito, com o horário da reserva existente no fuso local.</summary>
	public static Error Conflito(Reserva existente, RelogioDeSalas relogio)
	{
		var (_, inicio) = relogio.ParaLocal(existente.Inicio);
		var (_, fim) = relogio.ParaLocal(existente.Fim);

		return IntranetErrors.Salas.Conflito(existente.ReservadoPorNome, inicio, fim);
	}

	/// <summary>Metadados da trilha: o essencial, sem e-mails.</summary>
	public static string Metadata(Reserva reserva) =>
		JsonSerializer.Serialize(new Dictionary<string, object?>
		{
			["salaId"] = reserva.SalaId,
			["inicio"] = reserva.Inicio,
			["fim"] = reserva.Fim,
			["titulo"] = reserva.Titulo,
			["convidados"] = reserva.Convidados.Count,
		});

	private static bool NoPasso(TimeOnly hora) =>
		hora.Minute % Reserva.PassoEmMinutos == 0 && hora.Ticks % TimeSpan.TicksPerMinute == 0;
}
