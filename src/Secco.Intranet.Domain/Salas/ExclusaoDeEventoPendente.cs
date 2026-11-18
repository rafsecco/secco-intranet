using Secco.SharedKernel.Entities;

namespace Secco.Intranet.Domain.Salas;

/// <summary>
/// Evento da agenda externa que ainda precisa ser excluído porque a reserva dele foi cancelada.
/// Segue a mesma política de espera entre tentativas do espelhamento da <see cref="Reserva"/>.
/// </summary>
public sealed class ExclusaoDeEventoPendente : BaseEntity
{
	private ExclusaoDeEventoPendente()
	{
		// Construtor de rehidratação do EF Core
		EventoExternoId = string.Empty;
	}

	/// <summary>Cria uma exclusão pendente, pronta para a primeira tentativa.</summary>
	/// <param name="eventoExternoId">Identificador do evento na agenda externa.</param>
	public ExclusaoDeEventoPendente(string eventoExternoId)
	{
		EventoExternoId = eventoExternoId;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Identificador do evento na agenda externa (coluna <c>ds_evento_externo_id</c>)</summary>
	public string EventoExternoId { get; private set; }

	/// <summary>Tentativas que já falharam (coluna <c>nr_tentativas</c>)</summary>
	public int Tentativas { get; private set; }

	/// <summary>Momento a partir do qual vale tentar de novo; <c>null</c> para tentar já (coluna <c>dt_proxima_tentativa_em</c>)</summary>
	public DateTimeOffset? ProximaTentativaEm { get; private set; }

	/// <summary>Motivo da última falha (coluna <c>ds_motivo_da_falha</c>)</summary>
	public string? MotivoDaFalha { get; private set; }

	/// <summary>Indica que as tentativas se esgotaram ou o erro não tem conserto automático (coluna <c>fl_falhou</c>)</summary>
	public bool Falhou { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>)</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Registra uma falha que pode passar; agenda a próxima tentativa ou, na oitava, desiste.</summary>
	public void RegistrarFalhaPassageira(string motivo, DateTimeOffset agora)
	{
		Tentativas++;
		MotivoDaFalha = EsperaDeTentativas.Cortar(motivo);

		var espera = EsperaDeTentativas.Apos(Tentativas);
		if (espera is null)
		{
			Falhou = true;
			ProximaTentativaEm = null;
			return;
		}

		ProximaTentativaEm = agora + espera.Value;
	}

	/// <summary>Registra uma falha sem conserto automático (ex.: falta de permissão).</summary>
	public void MarcarFalhaDefinitiva(string motivo)
	{
		MotivoDaFalha = EsperaDeTentativas.Cortar(motivo);
		ProximaTentativaEm = null;
		Falhou = true;
	}
}
