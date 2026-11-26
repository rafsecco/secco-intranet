using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Application.Salas;

/// <summary>Persistência das reservas de sala e das exclusões de evento pendentes.</summary>
public interface IReservaRepository
{
	/// <summary>
	/// Abre uma transação, trava a linha da sala, roda <paramref name="acao"/> e confirma.
	/// Reverte tudo se a ação lançar. Serializa as gravações concorrentes na mesma sala.
	/// </summary>
	Task<T> ComSalaTravadaAsync<T>(Guid salaId, Func<CancellationToken, Task<T>> acao, CancellationToken ct = default);

	/// <summary>Primeira reserva da sala que ocupa tempo em comum com o intervalo; extremos que apenas se tocam não contam.</summary>
	Task<Reserva?> PrimeiraSobrepostaAsync(Guid salaId, DateTimeOffset inicio, DateTimeOffset fim, Guid? ignorarId, CancellationToken ct = default);

	/// <summary>Adiciona e grava uma reserva nova.</summary>
	Task AddAsync(Reserva reserva, CancellationToken ct = default);

	/// <summary>Obtém a reserva com os convidados, rastreada para edição.</summary>
	Task<Reserva?> GetAsync(Guid id, CancellationToken ct = default);

	/// <summary>Reservas que ocupam tempo em comum com o intervalo, com convidados, em ordem de início.</summary>
	Task<IReadOnlyList<Reserva>> ListarNoIntervaloAsync(DateTimeOffset de, DateTimeOffset ate, CancellationToken ct = default);

	/// <summary>Reservas que ainda não terminaram em <paramref name="agora"/> e em que a pessoa é dona ou convidada.</summary>
	Task<IReadOnlyList<Reserva>> ListarFuturasDaPessoaAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken ct = default);

	/// <summary>Reservas com espelhamento aguardando e sem espera ou com a espera vencida, rastreadas.</summary>
	Task<IReadOnlyList<Reserva>> ListarParaEspelharAsync(DateTimeOffset agora, int limite, CancellationToken ct = default);

	/// <summary>
	/// Grava as alterações pendentes. Devolve falso, sem gravar a reserva, quando ela foi alterada ou
	/// removida depois de lida (a versão mudou); a reserva continua rastreada, e <see cref="RecarregarAsync"/> a atualiza.
	/// </summary>
	Task<bool> TentarGravarAsync(CancellationToken ct = default);

	/// <summary>Relê a reserva rastreada do banco, descartando o que foi mudado nela; falso quando ela não existe mais.</summary>
	Task<bool> RecarregarAsync(Reserva reserva, CancellationToken ct = default);

	/// <summary>Marca a reserva para remoção (os convidados vão junto); a gravação é feita por <see cref="SaveChangesAsync"/>.</summary>
	void Remover(Reserva reserva);

	/// <summary>Registra, sem gravar, um evento externo que ainda precisa ser excluído.</summary>
	Task AdicionarExclusaoPendenteAsync(ExclusaoDeEventoPendente pendencia, CancellationToken ct = default);

	/// <summary>Exclusões pendentes ainda não esgotadas, sem espera ou com a espera vencida, rastreadas.</summary>
	Task<IReadOnlyList<ExclusaoDeEventoPendente>> ListarExclusoesPendentesAsync(DateTimeOffset agora, int limite, CancellationToken ct = default);

	/// <summary>Marca a exclusão pendente para remoção; a gravação é feita por <see cref="SaveChangesAsync"/>.</summary>
	void RemoverExclusaoPendente(ExclusaoDeEventoPendente pendencia);

	/// <summary>Grava as alterações pendentes.</summary>
	Task SaveChangesAsync(CancellationToken ct = default);
}
