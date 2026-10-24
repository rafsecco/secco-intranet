using Secco.SharedKernel.Entities;

namespace Secco.Intranet.Domain.Vencimentos;

/// <summary>Registro histórico de uma renovação de <see cref="ItemVencimento"/>.</summary>
public sealed class RenovacaoDeVencimento : BaseEntity
{
	private RenovacaoDeVencimento()
	{
		// Construtor de rehidratação do EF Core
		RenovadoPor = string.Empty;
	}

	internal RenovacaoDeVencimento(
		Guid itemVencimentoId,
		int cicloAnterior,
		DateOnly dataAnterior,
		DateOnly dataNova,
		string renovadoPor,
		DateTimeOffset renovadoEm,
		string? observacao)
	{
		ItemVencimentoId = itemVencimentoId;
		CicloAnterior = cicloAnterior;
		DataAnterior = dataAnterior;
		DataNova = dataNova;
		RenovadoPor = renovadoPor;
		RenovadoEm = renovadoEm;
		Observacao = observacao;
	}

	/// <summary>Item renovado (coluna <c>id_fk_item_vencimento</c>)</summary>
	public Guid ItemVencimentoId { get; private set; }

	/// <summary>Ciclo que terminou com a renovação (coluna <c>nr_ciclo_anterior</c>)</summary>
	public int CicloAnterior { get; private set; }

	/// <summary>Data de vencimento antes da renovação (coluna <c>dt_data_anterior</c>)</summary>
	public DateOnly DataAnterior { get; private set; }

	/// <summary>Data de vencimento depois da renovação (coluna <c>dt_data_nova</c>)</summary>
	public DateOnly DataNova { get; private set; }

	/// <summary>Quem renovou (coluna <c>ds_renovado_por</c>)</summary>
	public string RenovadoPor { get; private set; }

	/// <summary>Momento da renovação (coluna <c>dt_renovado_em</c>)</summary>
	public DateTimeOffset RenovadoEm { get; private set; }

	/// <summary>Observação livre. Opcional (coluna <c>ds_observacao</c>)</summary>
	public string? Observacao { get; private set; }
}
