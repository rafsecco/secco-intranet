using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Publicacoes;

/// <summary>
/// Publicação do mural, pertencente ao setor que a publicou. O estado — agendada, no ar,
/// expirada — não é guardado: sai da comparação de <see cref="PublicadoEm"/> e
/// <see cref="ExpiraEm"/> com o relógio. É o que dispensa job de transição e impede que
/// exista estado divergindo da realidade.
/// </summary>
public sealed class Publicacao : BaseEntity
{
	private Publicacao()
	{
		// Construtor de rehidratação do EF Core
		Titulo = string.Empty;
		Corpo = string.Empty;
		CriadoPor = string.Empty;
	}

	/// <summary>Cria uma publicação.</summary>
	/// <param name="setorId">Setor dono. Obrigatório.</param>
	/// <param name="titulo">Título de exibição. Obrigatório.</param>
	/// <param name="corpo">Corpo em Markdown cru. Obrigatório.</param>
	/// <param name="tipo">Natureza da publicação.</param>
	/// <param name="visibilidade">Quem enxerga.</param>
	/// <param name="prioridade">Urgência.</param>
	/// <param name="publicadoEm">Momento em que entra no ar; futuro significa agendada.</param>
	/// <param name="expiraEm">Momento em que sai do ar; nulo não expira.</param>
	/// <param name="criadoPor">Identificação de quem publicou.</param>
	/// <exception cref="DomainInvariantException">
	/// Se algum obrigatório faltar ou a expiração não for posterior à entrada no ar.
	/// </exception>
	public Publicacao(
		Guid setorId,
		string titulo,
		string corpo,
		TipoPublicacao tipo,
		Visibilidade visibilidade,
		PrioridadePublicacao prioridade,
		DateTimeOffset publicadoEm,
		DateTimeOffset? expiraEm,
		string criadoPor)
	{
		if (setorId == Guid.Empty)
		{
			throw new DomainInvariantException("Uma publicação exige um setor dono.");
		}

		Validar(titulo, corpo, publicadoEm, expiraEm);

		SetorId = setorId;
		Titulo = titulo.Trim();
		Corpo = corpo.Trim();
		Tipo = tipo;
		Visibilidade = visibilidade;
		Prioridade = prioridade;
		PublicadoEm = publicadoEm;
		ExpiraEm = expiraEm;
		CriadoPor = criadoPor;
		Ativo = true;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Setor dono (coluna <c>id_fk_setor</c>).</summary>
	public Guid SetorId { get; private set; }

	/// <summary>Título de exibição (coluna <c>ds_titulo</c>).</summary>
	public string Titulo { get; private set; }

	/// <summary>Corpo em Markdown cru — HTML nunca é persistido (coluna <c>ds_corpo</c>).</summary>
	public string Corpo { get; private set; }

	/// <summary>Natureza da publicação (coluna <c>ie_tipo</c>).</summary>
	public TipoPublicacao Tipo { get; private set; }

	/// <summary>Quem enxerga (coluna <c>ie_visibilidade</c>).</summary>
	public Visibilidade Visibilidade { get; private set; }

	/// <summary>Urgência (coluna <c>ie_prioridade</c>).</summary>
	public PrioridadePublicacao Prioridade { get; private set; }

	/// <summary>Momento de entrada no ar (coluna <c>dt_publicado_em</c>).</summary>
	public DateTimeOffset PublicadoEm { get; private set; }

	/// <summary>Momento de saída do ar; nulo não expira (coluna <c>dt_expira_em</c>).</summary>
	public DateTimeOffset? ExpiraEm { get; private set; }

	/// <summary>Publicação não arquivada (coluna <c>fl_ativo</c>).</summary>
	public bool Ativo { get; private set; }

	/// <summary>Quem publicou (coluna <c>ds_criado_por</c>).</summary>
	public string CriadoPor { get; private set; }

	/// <summary>Momento da criação do registro (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Momento da última edição (coluna <c>dt_atualizado_em</c>).</summary>
	public DateTimeOffset? AtualizadoEm { get; private set; }

	/// <summary>
	/// Responde se a publicação está no ar no instante informado. A mesma regra é traduzida
	/// para SQL na consulta do mural — as duas precisam concordar.
	/// </summary>
	/// <param name="agora">Instante de referência.</param>
	public bool EstaNoAr(DateTimeOffset agora) =>
		Ativo && PublicadoEm <= agora && (ExpiraEm is null || ExpiraEm > agora);

	/// <summary>Altera o conteúdo e o agendamento, revalidando as invariantes.</summary>
	/// <param name="titulo">Novo título.</param>
	/// <param name="corpo">Novo corpo em Markdown.</param>
	/// <param name="tipo">Nova natureza.</param>
	/// <param name="visibilidade">Nova visibilidade.</param>
	/// <param name="prioridade">Nova urgência.</param>
	/// <param name="publicadoEm">Nova entrada no ar.</param>
	/// <param name="expiraEm">Nova expiração.</param>
	/// <exception cref="DomainInvariantException">Nas mesmas condições do construtor.</exception>
	public void Editar(
		string titulo,
		string corpo,
		TipoPublicacao tipo,
		Visibilidade visibilidade,
		PrioridadePublicacao prioridade,
		DateTimeOffset publicadoEm,
		DateTimeOffset? expiraEm)
	{
		Validar(titulo, corpo, publicadoEm, expiraEm);

		Titulo = titulo.Trim();
		Corpo = corpo.Trim();
		Tipo = tipo;
		Visibilidade = visibilidade;
		Prioridade = prioridade;
		PublicadoEm = publicadoEm;
		ExpiraEm = expiraEm;
		AtualizadoEm = DateTimeOffset.UtcNow;
	}

	/// <summary>Tira a publicação de circulação, preservando o registro.</summary>
	public void Arquivar() => Ativo = false;

	private static void Validar(string titulo, string corpo, DateTimeOffset publicadoEm, DateTimeOffset? expiraEm)
	{
		if (string.IsNullOrWhiteSpace(titulo))
		{
			throw new DomainInvariantException("Uma publicação exige título não vazio.");
		}

		if (string.IsNullOrWhiteSpace(corpo))
		{
			throw new DomainInvariantException("Uma publicação exige corpo não vazio.");
		}

		if (expiraEm is not null && expiraEm <= publicadoEm)
		{
			throw new DomainInvariantException("A expiração precisa ser posterior à entrada no ar.");
		}
	}
}
