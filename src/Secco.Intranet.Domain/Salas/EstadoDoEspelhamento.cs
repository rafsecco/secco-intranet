namespace Secco.Intranet.Domain.Salas;

/// <summary>Situação do espelhamento de uma reserva na agenda externa.</summary>
public enum EstadoDoEspelhamento
{
	/// <summary>A reserva não é espelhada na agenda externa.</summary>
	NaoSeAplica = 0,

	/// <summary>Há trabalho de espelhamento pendente (criar ou atualizar o evento).</summary>
	Aguardando = 1,

	/// <summary>O evento externo está em dia com a reserva.</summary>
	Espelhada = 2,

	/// <summary>As tentativas se esgotaram ou o erro não tem conserto automático.</summary>
	Falhou = 3
}
