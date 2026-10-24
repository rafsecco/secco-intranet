using Secco.SharedKernel.Entities;

namespace Secco.Intranet.Domain.Vencimentos;

/// <summary>Desfecho do tratamento de um marco de aviso.</summary>
public enum ResultadoDoAviso
{
	/// <summary>Aviso enviado.</summary>
	Enviado = 0,

	/// <summary>Marco pulado: já passou sem aviso (cadastro tardio ou marco mais distante que um já tratado).</summary>
	Pulado = 1,

	/// <summary>Sem destinatário para receber o aviso.</summary>
	SemDestinatario = 2,
}

/// <summary>
/// Registro de que um marco de um ciclo foi tratado — garante que o mesmo aviso não se repita.
/// </summary>
public sealed class AvisoDeVencimentoEnviado : BaseEntity
{
	private AvisoDeVencimentoEnviado()
	{
		// Construtor de rehidratação do EF Core
	}

	/// <summary>Registra o tratamento de um marco.</summary>
	public AvisoDeVencimentoEnviado(
		Guid itemVencimentoId,
		int ciclo,
		int marco,
		ResultadoDoAviso resultado,
		int quantidadeDeDestinos,
		DateTimeOffset tratadoEm)
	{
		ItemVencimentoId = itemVencimentoId;
		Ciclo = ciclo;
		Marco = marco;
		Resultado = resultado;
		QuantidadeDeDestinos = quantidadeDeDestinos;
		TratadoEm = tratadoEm;
	}

	/// <summary>Item avisado.</summary>
	public Guid ItemVencimentoId { get; private set; }

	/// <summary>Ciclo do item em que o marco foi tratado.</summary>
	public int Ciclo { get; private set; }

	/// <summary>Marco em dias antes do vencimento (0 = no dia).</summary>
	public int Marco { get; private set; }

	/// <summary>Desfecho do tratamento.</summary>
	public ResultadoDoAviso Resultado { get; private set; }

	/// <summary>Quantidade de destinatários alcançados.</summary>
	public int QuantidadeDeDestinos { get; private set; }

	/// <summary>Momento do tratamento.</summary>
	public DateTimeOffset TratadoEm { get; private set; }
}
