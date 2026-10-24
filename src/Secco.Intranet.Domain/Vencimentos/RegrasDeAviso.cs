namespace Secco.Intranet.Domain.Vencimentos;

/// <summary>Situação de um vencimento em relação a hoje.</summary>
public enum EstadoDoVencimento
{
	/// <summary>Ainda fora da janela do primeiro aviso.</summary>
	EmDia,

	/// <summary>Dentro da janela da maior antecedência, antes do dia do vencimento.</summary>
	AVencer,

	/// <summary>Vence hoje.</summary>
	VenceHoje,

	/// <summary>Data de vencimento já passou.</summary>
	Vencido,

	/// <summary>Item encerrado.</summary>
	Encerrado,
}

/// <summary>
/// Decisão do dia para um item: qual marco avisar (no máximo um) e quais marcos devidos
/// devem ser registrados como pulados.
/// </summary>
/// <param name="MarcoAEnviar">Marco a avisar hoje, ou <c>null</c> se nenhum.</param>
/// <param name="MarcosAPular">Marcos devidos que não serão avisados e devem ser registrados como pulados.</param>
/// <param name="DiasRestantes">Dias até o vencimento; negativo quando vencido.</param>
public sealed record DecisaoDeAviso(int? MarcoAEnviar, IReadOnlyList<int> MarcosAPular, int DiasRestantes);

/// <summary>Regras puras dos marcos de aviso de vencimento.</summary>
public static class RegrasDeAviso
{
	/// <summary>Calcula o estado do vencimento em <paramref name="hoje"/>.</summary>
	public static EstadoDoVencimento Estado(ItemVencimento item, DateOnly hoje)
	{
		if (item.EstaEncerrado)
		{
			return EstadoDoVencimento.Encerrado;
		}

		if (hoje > item.DataVencimento)
		{
			return EstadoDoVencimento.Vencido;
		}

		if (hoje == item.DataVencimento)
		{
			return EstadoDoVencimento.VenceHoje;
		}

		return hoje >= item.DataVencimento.AddDays(-item.Antecedencias.Max())
			? EstadoDoVencimento.AVencer
			: EstadoDoVencimento.EmDia;
	}

	/// <summary>
	/// Decide o que fazer hoje com o item: avisa só o marco devido mais próximo do vencimento e
	/// pula os demais pendentes. O marco 0 só é avisado no próprio dia, e nenhum marco mais
	/// distante que um já tratado é avisado.
	/// </summary>
	/// <param name="item">Item avaliado.</param>
	/// <param name="hoje">Data corrente no fuso configurado.</param>
	/// <param name="marcosTratadosNoCiclo">Marcos já tratados (enviados, pulados ou sem destinatário) no ciclo atual.</param>
	public static DecisaoDeAviso Decidir(ItemVencimento item, DateOnly hoje, IReadOnlySet<int> marcosTratadosNoCiclo)
	{
		var dias = item.DataVencimento.DayNumber - hoje.DayNumber;

		if (item.EstaEncerrado)
		{
			return new DecisaoDeAviso(null, [], dias);
		}

		var pendentes = MarcosDevidos(item, hoje)
			.Where(m => !marcosTratadosNoCiclo.Contains(m))
			.OrderBy(m => m)
			.ToList();

		if (pendentes.Count == 0)
		{
			return new DecisaoDeAviso(null, [], dias);
		}

		var candidato = pendentes[0];

		if (candidato == 0 && hoje != item.DataVencimento)
		{
			return new DecisaoDeAviso(null, pendentes, dias);
		}

		if (marcosTratadosNoCiclo.Any(t => t < candidato))
		{
			return new DecisaoDeAviso(null, pendentes, dias);
		}

		return new DecisaoDeAviso(candidato, pendentes.Skip(1).ToList(), dias);
	}

	/// <summary>
	/// Próximos avisos do ciclo atual (marcos ainda não tratados cuja data é hoje ou futura),
	/// em ordem crescente de data. Vazio para item encerrado.
	/// </summary>
	public static IReadOnlyList<(int Marco, DateOnly Data)> ProximosAvisos(ItemVencimento item, DateOnly hoje, IReadOnlySet<int> marcosTratadosNoCiclo)
	{
		if (item.EstaEncerrado)
		{
			return [];
		}

		return item.Antecedencias
			.Append(0)
			.Where(m => !marcosTratadosNoCiclo.Contains(m))
			.Select(m => (Marco: m, Data: item.DataVencimento.AddDays(-m)))
			.Where(x => x.Data >= hoje)
			.OrderBy(x => x.Data)
			.ToList();
	}

	private static IEnumerable<int> MarcosDevidos(ItemVencimento item, DateOnly hoje)
	{
		foreach (var n in item.Antecedencias)
		{
			if (hoje >= item.DataVencimento.AddDays(-n))
			{
				yield return n;
			}
		}

		if (hoje >= item.DataVencimento)
		{
			yield return 0;
		}
	}
}
