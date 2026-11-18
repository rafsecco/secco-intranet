using Secco.SharedKernel.Entities;

namespace Secco.Intranet.Domain.Salas;

/// <summary>Pessoa convidada para uma reserva de sala.</summary>
public sealed class ConvidadoDaReserva : BaseEntity
{
	private ConvidadoDaReserva()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
	}

	/// <summary>Cria um convidado. O vínculo com a reserva é feito pela própria <see cref="Reserva"/>.</summary>
	public ConvidadoDaReserva(Guid usuarioId, string nome, string? email)
	{
		UsuarioId = usuarioId;
		Nome = nome;
		Email = email;
	}

	/// <summary>Reserva a que o convidado pertence (coluna <c>id_fk_reserva</c>)</summary>
	public Guid ReservaId { get; internal set; }

	/// <summary>Usuário convidado (coluna <c>id_fk_usuario</c>)</summary>
	public Guid UsuarioId { get; private set; }

	/// <summary>Nome do convidado no momento do convite (coluna <c>ds_nome</c>)</summary>
	public string Nome { get; private set; }

	/// <summary>E-mail do convidado no momento do convite (coluna <c>ds_email</c>)</summary>
	public string? Email { get; private set; }
}
