using System.Text.RegularExpressions;
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Setores;

/// <summary>
/// Setor/departamento da instituição (ex: Financeiro, RH, Infraestrutura). É o eixo
/// organizacional da Intranet: documentos, processos e outros recursos são habilitados
/// por setor, e o acesso de cada usuário é resolvido via as Roles tenant-scoped
/// <c>{slug}-admin</c> e <c>{slug}-user</c> criadas no Secco.SecureGate quando o setor
/// é cadastrado (ver Secco.Intranet.Infrastructure.Access).
/// </summary>
public sealed class Setor : BaseEntity
{
	private Setor()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
		Slug = string.Empty;
		Icone = IconePadrao;
	}

	/// <summary>Cria um setor.</summary>
	/// <param name="nome">Nome de exibição. Obrigatório.</param>
	/// <param name="slug">Identificador curto usado em rotas e nas Roles do SecureGate. Obrigatório.</param>
	/// <param name="fixo">
	/// Quando <c>true</c>, o setor não pode ser desabilitado nem excluído pela tela de
	/// administração (caso do setor de Infraestrutura, dono nato do recurso de Inventário).
	/// </param>
	/// <param name="icone">
	/// Classe do Bootstrap Icons exibida no menu, como <c>bi-cash-coin</c>. Vazio usa
	/// <see cref="IconePadrao"/>.
	/// </param>
	/// <exception cref="DomainInvariantException">
	/// Se nome ou slug forem nulos ou vazios, ou se o ícone não for uma classe do Bootstrap
	/// Icons.
	/// </exception>
	public Setor(string nome, string slug, bool fixo = false, string? icone = null)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Um setor exige nome não vazio.");
		}

		if (string.IsNullOrWhiteSpace(slug))
		{
			throw new DomainInvariantException("Um setor exige slug não vazio.");
		}

		Nome = nome;
		Slug = slug.Trim().ToLowerInvariant();
		Fixo = fixo;
		Icone = NormalizarIcone(icone);
		Ativo = true;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Ícone usado quando o setor não escolhe um.</summary>
	public const string IconePadrao = "bi-diagram-3";

	/// <summary>
	/// Só o formato do Bootstrap Icons entra. A validação existe porque o valor vai direto
	/// para o atributo <c>class</c> do item de menu: sem ela, qualquer texto viraria classe
	/// CSS arbitrária, e um <c>d-none</c> digitado por engano sumiria com o próprio item.
	/// </summary>
	private static readonly Regex FormatoDoIcone = new("^bi-[a-z0-9-]+$", RegexOptions.Compiled);

	/// <summary>
	/// Indica se o ícone informado é aceitável. Existe para a camada de aplicação decidir sem
	/// provocar exceção — entrada de usuário vira <c>Result</c> (ADR-0004).
	/// </summary>
	/// <param name="icone">Classe informada; vazio é válido e cai no padrão.</param>
	public static bool IconeEhValido(string? icone) =>
		string.IsNullOrWhiteSpace(icone) || FormatoDoIcone.IsMatch(icone.Trim().ToLowerInvariant());

	private static string NormalizarIcone(string? icone)
	{
		if (string.IsNullOrWhiteSpace(icone))
		{
			return IconePadrao;
		}

		var normalizado = icone.Trim().ToLowerInvariant();

		return FormatoDoIcone.IsMatch(normalizado)
			? normalizado
			: throw new DomainInvariantException(
				"O ícone precisa ser uma classe do Bootstrap Icons, como 'bi-cash-coin'.");
	}

	/// <summary>Nome de exibição (coluna <c>ds_nome</c>).</summary>
	public string Nome { get; private set; }

	/// <summary>
	/// Identificador curto, único por tenant — base do nome das Roles no SecureGate
	/// (<c>{slug}-admin</c> / <c>{slug}-user</c>) e das rotas (coluna <c>ds_slug</c>).
	/// </summary>
	public string Slug { get; private set; }

	/// <summary>
	/// Setor fixo do sistema (ex: Infraestrutura) — não pode ser desabilitado nem excluído
	/// (coluna <c>fl_fixo</c>).
	/// </summary>
	public bool Fixo { get; private set; }

	/// <summary>
	/// Classe do Bootstrap Icons exibida no menu (coluna <c>ds_icone</c>).
	/// </summary>
	public string Icone { get; private set; } = IconePadrao;

	/// <summary>Setor ativo (coluna <c>fl_ativo</c>).</summary>
	public bool Ativo { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Renomeia o setor.</summary>
	/// <param name="nome">Novo nome de exibição. Obrigatório.</param>
	/// <exception cref="DomainInvariantException">Se o nome for nulo ou vazio.</exception>
	public void Renomear(string nome)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Um setor exige nome não vazio.");
		}

		Nome = nome;
	}

	/// <summary>
	/// Desativa o setor (deixa de aparecer no menu e nas telas de vínculo de usuário).
	/// </summary>
	/// <exception cref="DomainInvariantException">Se o setor for fixo.</exception>
	public void Desativar()
	{
		if (Fixo)
		{
			throw new DomainInvariantException("Um setor fixo não pode ser desativado.");
		}

		Ativo = false;
	}

	/// <summary>Reativa o setor.</summary>
	public void Ativar() => Ativo = true;

	/// <summary>Troca o ícone exibido no menu.</summary>
	/// <param name="icone">Classe do Bootstrap Icons; vazio volta ao <see cref="IconePadrao"/>.</param>
	/// <exception cref="DomainInvariantException">Se o ícone não for uma classe do Bootstrap Icons.</exception>
	public void DefinirIcone(string? icone) => Icone = NormalizarIcone(icone);
}
