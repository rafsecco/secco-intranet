using System.Text.RegularExpressions;
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Menu;

/// <summary>Tipo de um nó da árvore de menu de um setor.</summary>
public enum TipoDeItemMenu
{
	/// <summary>Linha raiz que representa o próprio Setor — única por setor, nunca criável/excluível pela tela.</summary>
	Setor = 0,

	/// <summary>Repositório de documentos do setor.</summary>
	Documentos = 1,

	/// <summary>Avisos (publicações) do setor.</summary>
	Avisos = 2,

	/// <summary>Item livre — rótulo e uma rota opcional, sem recurso embutido.</summary>
	Personalizado = 3,
}

/// <summary>
/// Nó de uma árvore autorrecursiva de itens de menu, escopada a um Setor. A raiz
/// (<see cref="TipoDeItemMenu.Setor"/>) nasce junto com o <c>Setor</c>; Documentos, Avisos e
/// itens <c>Personalizado</c> são sempre descendentes dela. <see cref="ParentId"/> só é
/// definido na criação — por isso a árvore é acíclica sem checagem.
/// </summary>
public sealed class ItemMenu : BaseEntity
{
	private static readonly Regex FormatoDoIcone = new("^bi-[a-z0-9-]+$", RegexOptions.Compiled);

	private ItemMenu()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
		Slug = string.Empty;
	}

	/// <summary>Cria um nó da árvore.</summary>
	/// <param name="setorId">Setor dono da árvore. Denormalizado em toda linha, inclusive a raiz.</param>
	/// <param name="parentId">Pai na árvore; nulo só na linha raiz.</param>
	/// <param name="nome">Rótulo de exibição. Obrigatório sempre, mesmo em <c>Personalizado</c>.</param>
	/// <param name="slug">Identificador único entre irmãos, usado na URL.</param>
	/// <param name="tipo">Tipo do nó.</param>
	/// <param name="rota">Link opcional, só relevante em <see cref="TipoDeItemMenu.Personalizado"/>.</param>
	/// <param name="icone">Classe do Bootstrap Icons; vazio/nulo = sem ícone.</param>
	/// <param name="ordem">Posição entre irmãos.</param>
	/// <exception cref="DomainInvariantException">Nome/slug vazios, ou ícone fora do formato.</exception>
	public ItemMenu(
		Guid setorId, Guid? parentId, string nome, string slug, TipoDeItemMenu tipo, string? rota, string? icone, int ordem)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Um item de menu exige nome não vazio.");
		}

		if (string.IsNullOrWhiteSpace(slug))
		{
			throw new DomainInvariantException("Um item de menu exige slug não vazio.");
		}

		if (!IconeEhValido(icone))
		{
			throw new DomainInvariantException("O ícone precisa ser uma classe do Bootstrap Icons, como 'bi-cash-coin'.");
		}

		SetorId = setorId;
		ParentId = parentId;
		Nome = nome;
		Slug = slug.Trim().ToLowerInvariant();
		Tipo = tipo;
		Rota = string.IsNullOrWhiteSpace(rota) ? null : rota.Trim();
		Icone = string.IsNullOrWhiteSpace(icone) ? null : icone.Trim().ToLowerInvariant();
		Ordem = ordem;
		Ativo = true;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Setor dono da árvore.</summary>
	public Guid SetorId { get; private set; }

	/// <summary>Pai na árvore; nulo só na linha raiz.</summary>
	public Guid? ParentId { get; private set; }

	/// <summary>Rótulo de exibição.</summary>
	public string Nome { get; private set; }

	/// <summary>Identificador único entre irmãos — segmento da URL.</summary>
	public string Slug { get; private set; }

	/// <summary>Tipo do nó.</summary>
	public TipoDeItemMenu Tipo { get; private set; }

	/// <summary>Link opcional, só relevante em <see cref="TipoDeItemMenu.Personalizado"/>.</summary>
	public string? Rota { get; private set; }

	/// <summary>Classe do Bootstrap Icons; nulo = sem ícone.</summary>
	public string? Icone { get; private set; }

	/// <summary>Posição entre irmãos.</summary>
	public int Ordem { get; private set; }

	/// <summary>Ativo — some da árvore quando <c>false</c>, sem apagar.</summary>
	public bool Ativo { get; private set; }

	/// <summary>Momento da criação.</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Indica se o ícone informado é aceitável — vazio é válido (sem ícone).</summary>
	/// <param name="icone">Classe informada.</param>
	public static bool IconeEhValido(string? icone) =>
		string.IsNullOrWhiteSpace(icone) || FormatoDoIcone.IsMatch(icone.Trim().ToLowerInvariant());

	/// <summary>Desativa o item — some da árvore, sem apagar.</summary>
	public void Desativar() => Ativo = false;

	/// <summary>Reativa o item.</summary>
	public void Ativar() => Ativo = true;

	/// <summary>Troca a posição entre irmãos.</summary>
	/// <param name="ordem">Nova posição.</param>
	public void DefinirOrdem(int ordem) => Ordem = ordem;
}
