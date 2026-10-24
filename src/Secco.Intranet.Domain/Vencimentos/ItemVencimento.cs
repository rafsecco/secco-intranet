using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Vencimentos;

/// <summary>
/// Item com data de vencimento acompanhada por um setor (certificado, contrato, licença...).
/// A cada mudança de data (edição, renovação, reativação) abre-se um novo <see cref="Ciclo"/>,
/// e os avisos já tratados num ciclo não valem para o seguinte.
/// </summary>
public sealed class ItemVencimento : BaseEntity
{
	/// <summary>Antecedências (em dias) usadas quando o cadastro não informa nenhuma.</summary>
	public static readonly IReadOnlyList<int> AntecedenciasPadrao = [30, 7, 1];

	private const int MaximoDeAntecedencias = 6;
	private const int MenorAntecedencia = 1;
	private const int MaiorAntecedencia = 365;

	private ItemVencimento()
	{
		// Construtor de rehidratação do EF Core
		Titulo = string.Empty;
		Antecedencias = [];
	}

	/// <summary>Cria um item de vencimento no ciclo 1. A data pode estar no passado.</summary>
	/// <param name="setorId">Setor dono do item.</param>
	/// <param name="titulo">Título de exibição. Obrigatório.</param>
	/// <param name="descricao">Descrição livre. Opcional.</param>
	/// <param name="dataVencimento">Data do vencimento.</param>
	/// <param name="responsavelId">Usuário responsável, destinatário dos avisos. Opcional.</param>
	/// <param name="antecedencias">Marcos em dias antes do vencimento; <c>null</c> usa <see cref="AntecedenciasPadrao"/>.</param>
	/// <exception cref="DomainInvariantException">Título vazio ou antecedências inválidas.</exception>
	public ItemVencimento(
		Guid setorId,
		string titulo,
		string? descricao,
		DateOnly dataVencimento,
		Guid? responsavelId,
		IReadOnlyList<int>? antecedencias)
	{
		ValidarTitulo(titulo);

		SetorId = setorId;
		Titulo = titulo;
		Descricao = descricao;
		DataVencimento = dataVencimento;
		ResponsavelId = responsavelId;
		Antecedencias = NormalizarAntecedencias(antecedencias ?? AntecedenciasPadrao);
		Ciclo = 1;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Setor dono do item.</summary>
	public Guid SetorId { get; private set; }

	/// <summary>Título de exibição.</summary>
	public string Titulo { get; private set; }

	/// <summary>Descrição livre.</summary>
	public string? Descricao { get; private set; }

	/// <summary>Data do vencimento do ciclo atual.</summary>
	public DateOnly DataVencimento { get; private set; }

	/// <summary>Usuário responsável, destinatário dos avisos.</summary>
	public Guid? ResponsavelId { get; private set; }

	/// <summary>Marcos de aviso em dias antes do vencimento, em ordem decrescente. O marco 0 (no dia) é implícito.</summary>
	public IReadOnlyList<int> Antecedencias { get; private set; }

	/// <summary>Ciclo de aviso atual; nasce em 1 e sobe a cada mudança de data.</summary>
	public int Ciclo { get; private set; }

	/// <summary>Momento do encerramento; <c>null</c> enquanto o item está ativo.</summary>
	public DateTimeOffset? EncerradoEm { get; private set; }

	/// <summary>Quem encerrou o item; <c>null</c> enquanto está ativo.</summary>
	public string? EncerradoPor { get; private set; }

	/// <summary>Momento da criação.</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Indica se o item foi encerrado.</summary>
	public bool EstaEncerrado => EncerradoEm is not null;

	/// <summary>Altera os dados do item. Mudar a data abre um ciclo novo.</summary>
	/// <returns><c>true</c> quando a data mudou (e portanto o ciclo foi incrementado).</returns>
	/// <exception cref="DomainInvariantException">Item encerrado, título vazio ou antecedências inválidas.</exception>
	public bool Editar(string titulo, string? descricao, DateOnly dataVencimento, Guid? responsavelId, IReadOnlyList<int> antecedencias)
	{
		GarantirAtivo();
		ValidarTitulo(titulo);
		var normalizadas = NormalizarAntecedencias(antecedencias);

		var dataMudou = dataVencimento != DataVencimento;

		Titulo = titulo;
		Descricao = descricao;
		ResponsavelId = responsavelId;
		Antecedencias = normalizadas;

		if (dataMudou)
		{
			DataVencimento = dataVencimento;
			Ciclo++;
		}

		return dataMudou;
	}

	/// <summary>Renova o vencimento para uma data posterior, abrindo um ciclo novo.</summary>
	/// <returns>O registro histórico da renovação, a ser persistido por quem chama.</returns>
	/// <exception cref="DomainInvariantException">
	/// Item encerrado, ou nova data que não seja posterior à data atual e a <paramref name="hoje"/>.
	/// </exception>
	public RenovacaoDeVencimento Renovar(DateOnly novaData, DateOnly hoje, string renovadoPor, string? observacao)
	{
		GarantirAtivo();

		if (novaData <= DataVencimento)
		{
			throw new DomainInvariantException("A nova data deve ser posterior ao vencimento atual.");
		}

		if (novaData <= hoje)
		{
			throw new DomainInvariantException("A nova data deve ser posterior a hoje.");
		}

		var renovacao = new RenovacaoDeVencimento(Id, Ciclo, DataVencimento, novaData, renovadoPor, DateTimeOffset.UtcNow, observacao);

		DataVencimento = novaData;
		Ciclo++;

		return renovacao;
	}

	/// <summary>Encerra o item. Idempotente: encerrar de novo não altera o primeiro registro.</summary>
	public void Encerrar(string encerradoPor, DateTimeOffset agora)
	{
		if (EstaEncerrado)
		{
			return;
		}

		EncerradoEm = agora;
		EncerradoPor = encerradoPor;
	}

	/// <summary>Reativa um item encerrado com uma nova data futura, abrindo um ciclo novo.</summary>
	/// <exception cref="DomainInvariantException">A nova data não é posterior a <paramref name="hoje"/>.</exception>
	public void Reativar(DateOnly novaData, DateOnly hoje)
	{
		if (novaData <= hoje)
		{
			throw new DomainInvariantException("A nova data deve ser posterior a hoje.");
		}

		EncerradoEm = null;
		EncerradoPor = null;
		DataVencimento = novaData;
		Ciclo++;
	}

	private void GarantirAtivo()
	{
		if (EstaEncerrado)
		{
			throw new DomainInvariantException("Um item de vencimento encerrado não aceita alterações.");
		}
	}

	private static void ValidarTitulo(string titulo)
	{
		if (string.IsNullOrWhiteSpace(titulo))
		{
			throw new DomainInvariantException("Um item de vencimento exige título não vazio.");
		}
	}

	private static IReadOnlyList<int> NormalizarAntecedencias(IReadOnlyList<int> antecedencias)
	{
		if (antecedencias.Count is 0 or > MaximoDeAntecedencias)
		{
			throw new DomainInvariantException($"Informe de 1 a {MaximoDeAntecedencias} antecedências.");
		}

		if (antecedencias.Any(a => a < MenorAntecedencia || a > MaiorAntecedencia))
		{
			throw new DomainInvariantException($"Cada antecedência deve estar entre {MenorAntecedencia} e {MaiorAntecedencia} dias.");
		}

		if (antecedencias.Distinct().Count() != antecedencias.Count)
		{
			throw new DomainInvariantException("As antecedências não podem se repetir.");
		}

		return antecedencias.OrderByDescending(a => a).ToArray();
	}
}
