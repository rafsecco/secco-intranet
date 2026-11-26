namespace Secco.Intranet.Application.Salas.Agenda;

/// <summary>Pessoa convidada para o evento da agenda externa.</summary>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">Endereço de e-mail.</param>
public sealed record ParticipanteDeAgenda(string Nome, string Email);

/// <summary>Evento a criar ou atualizar na agenda externa.</summary>
/// <param name="ChaveDeIdempotencia">Chave estável por reserva: repetir a criação com a mesma chave não pode duplicar o evento.</param>
/// <param name="Assunto">Assunto do evento.</param>
/// <param name="Corpo">Texto do evento.</param>
/// <param name="Local">Local do evento (nome da sala).</param>
/// <param name="Inicio">Início, em UTC.</param>
/// <param name="Fim">Fim, em UTC.</param>
/// <param name="Participantes">Pessoas convidadas, sem e-mail repetido.</param>
/// <param name="EmailDaSala">E-mail da sala como recurso, quando tem.</param>
public sealed record EventoDeAgenda(
	string ChaveDeIdempotencia,
	string Assunto,
	string Corpo,
	string Local,
	DateTimeOffset Inicio,
	DateTimeOffset Fim,
	IReadOnlyList<ParticipanteDeAgenda> Participantes,
	string? EmailDaSala);

/// <summary>Como terminou uma chamada à agenda externa.</summary>
public enum DesfechoDaAgenda
{
	/// <summary>A operação foi feita.</summary>
	Feito,

	/// <summary>Falha que pode passar (rede, limite de chamadas, serviço fora); vale tentar de novo depois.</summary>
	FalhaPassageira,

	/// <summary>Falha sem conserto automático (permissão negada, caixa inexistente).</summary>
	FalhaDefinitiva
}

/// <summary>Resultado de uma chamada à agenda externa.</summary>
/// <param name="Desfecho">Como terminou.</param>
/// <param name="EventoId">Identificador do evento na agenda externa, quando a operação o devolve.</param>
/// <param name="Motivo">Motivo da falha, em texto que pode ser mostrado na tela; nunca carrega segredo.</param>
public sealed record ResultadoDaAgenda(DesfechoDaAgenda Desfecho, string? EventoId = null, string? Motivo = null);

/// <summary>Agenda externa onde as reservas são espelhadas; o espelhamento não sabe qual é o provedor.</summary>
public interface IAgendaExterna
{
	/// <summary>Cria o evento. Repetir com a mesma <see cref="EventoDeAgenda.ChaveDeIdempotencia"/> não duplica.</summary>
	Task<ResultadoDaAgenda> CriarAsync(EventoDeAgenda evento, CancellationToken ct = default);

	/// <summary>Atualiza o evento já criado.</summary>
	Task<ResultadoDaAgenda> AtualizarAsync(string eventoId, EventoDeAgenda evento, CancellationToken ct = default);

	/// <summary>Apaga o evento; a agenda externa se encarrega de avisar os convidados.</summary>
	Task<ResultadoDaAgenda> ApagarAsync(string eventoId, CancellationToken ct = default);
}
