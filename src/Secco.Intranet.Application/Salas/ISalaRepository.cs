using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Application.Salas;

/// <summary>Persistência das salas no banco do tenant atual.</summary>
public interface ISalaRepository
{
	/// <summary>Adiciona e grava uma sala nova.</summary>
	Task AddAsync(Sala sala, CancellationToken ct = default);

	/// <summary>Obtém a sala pelo id, rastreada para edição.</summary>
	Task<Sala?> GetAsync(Guid id, CancellationToken ct = default);

	/// <summary>Lista as salas por <see cref="Sala.Ordem"/> e depois por nome.</summary>
	Task<IReadOnlyList<Sala>> ListarAsync(bool somenteAtivas, CancellationToken ct = default);

	/// <summary>Indica se já existe sala com o nome, ignorando a sala <paramref name="ignorarId"/> quando informada.</summary>
	Task<bool> NomeEmUsoAsync(string nome, Guid? ignorarId, CancellationToken ct = default);

	/// <summary>Indica se a sala tem alguma reserva que ainda não terminou em <paramref name="agora"/>.</summary>
	Task<bool> TemReservaFuturaAsync(Guid salaId, DateTimeOffset agora, CancellationToken ct = default);

	/// <summary>Apaga de imediato todas as reservas da sala (os convidados vão pela cascata do banco).</summary>
	Task RemoverReservasDaSalaAsync(Guid salaId, CancellationToken ct = default);

	/// <summary>Marca a sala para remoção; a gravação é feita por <see cref="SaveChangesAsync"/>.</summary>
	void Remover(Sala sala);

	/// <summary>Grava as alterações pendentes.</summary>
	Task SaveChangesAsync(CancellationToken ct = default);
}
