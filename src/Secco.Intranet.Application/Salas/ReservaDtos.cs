using Secco.Intranet.Domain.Salas;

namespace Secco.Intranet.Application.Salas;

/// <summary>Quem faz o pedido; <c>EhAdministrador</c> é decidido pela camada web.</summary>
public sealed record QuemPede(Guid UsuarioId, bool EhAdministrador);

/// <summary>Pedido de reserva de sala; horários no fuso configurado, no mesmo dia.</summary>
public sealed record ReservarSalaCommand(
	Guid SalaId,
	DateOnly Dia,
	TimeOnly Inicio,
	TimeOnly Fim,
	string? Titulo,
	string? Descricao,
	IReadOnlyList<Guid> Convidados,
	QuemPede Quem);

/// <summary>Pedido de alteração de uma reserva.</summary>
public sealed record AlterarReservaCommand(
	Guid Id,
	Guid SalaId,
	DateOnly Dia,
	TimeOnly Inicio,
	TimeOnly Fim,
	string? Titulo,
	string? Descricao,
	IReadOnlyList<Guid> Convidados,
	QuemPede Quem);

/// <summary>Pessoa ligada a uma reserva, com o nome copiado no momento da gravação.</summary>
public sealed record PessoaDaReservaDto(Guid UsuarioId, string Nome);

/// <summary>Reserva como a interface a enxerga, com horários no fuso configurado.</summary>
public sealed record ReservaDto(
	Guid Id,
	Guid SalaId,
	string SalaNome,
	DateOnly Dia,
	TimeOnly Inicio,
	TimeOnly Fim,
	string Titulo,
	string? Descricao,
	PessoaDaReservaDto ReservadoPor,
	IReadOnlyList<PessoaDaReservaDto> Convidados,
	bool PodeAlterar,
	EstadoDoEspelhamento Espelhamento,
	string? MotivoDaFalha);

/// <summary>Ocupação das salas ativas num dia.</summary>
public sealed record OcupacaoDoDiaDto(DateOnly Dia, IReadOnlyList<SalaDto> Salas, IReadOnlyList<ReservaDto> Reservas);
