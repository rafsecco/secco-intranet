using System.Text.RegularExpressions;
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Bibliotecas;

/// <summary>
/// Biblioteca de documentos: coleção de documentos sem setor dono. O acesso é concedido por
/// permissões próprias (<c>biblioteca-{slug}:read</c> e <c>biblioteca-{slug}:write</c>), não pelas
/// Roles de setor.
/// </summary>
public sealed partial class Biblioteca : BaseEntity
{
	private Biblioteca()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
		Slug = string.Empty;
		Icone = IconePadrao;
	}

	/// <summary>Cria uma biblioteca.</summary>
	/// <param name="nome">Nome de exibição. Obrigatório.</param>
	/// <param name="slug">Identificador curto usado em rotas e nas permissões. Obrigatório.</param>
	/// <param name="icone">
	/// Classe do Bootstrap Icons exibida no menu. Vazio usa <see cref="IconePadrao"/>.
	/// </param>
	/// <exception cref="DomainInvariantException">
	/// Se nome ou slug forem inválidos, ou se o ícone não for uma classe do Bootstrap Icons.
	/// </exception>
	public Biblioteca(string nome, string slug, string? icone = null)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Uma biblioteca exige nome não vazio.");
		}

		if (!SlugEhValido(slug))
		{
			throw new DomainInvariantException(
				$"O slug da biblioteca deve ter de 1 a {TamanhoMaximoDoSlug} caracteres entre a-z, 0-9 e hífen.");
		}

		Nome = nome;
		Slug = slug.Trim().ToLowerInvariant();
		Icone = NormalizarIcone(icone);
		Ativo = true;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Ícone usado quando a biblioteca não escolhe um.</summary>
	public const string IconePadrao = "bi-journal-text";

	/// <summary>Tamanho máximo do slug, que cabe no nome das permissões e da coluna.</summary>
	public const int TamanhoMaximoDoSlug = 53;

	[GeneratedRegex(@"\A[a-z0-9-]{1,53}\z")]
	private static partial Regex PadraoDoSlug();

	/// <summary>
	/// Indica se o slug é aceitável (após trim e minúsculas): <c>[a-z0-9-]</c>, de 1 a
	/// <see cref="TamanhoMaximoDoSlug"/> caracteres. Entrada de usuário vira <c>Result</c> (ADR-0004).
	/// </summary>
	/// <param name="slug">Slug informado.</param>
	public static bool SlugEhValido(string? slug) =>
		slug is not null && PadraoDoSlug().IsMatch(slug.Trim().ToLowerInvariant());

	/// <summary>
	/// Indica se o ícone informado é aceitável (ver <see cref="IconeBootstrap"/>); vazio é válido e
	/// cai no padrão.
	/// </summary>
	/// <param name="icone">Ícone informado.</param>
	public static bool IconeEhValido(string? icone) => IconeBootstrap.TentarNormalizar(icone, out _);

	private static string NormalizarIcone(string? icone) =>
		IconeBootstrap.TentarNormalizar(icone, out var normalizado)
			? normalizado ?? IconePadrao
			: throw new DomainInvariantException(
				"O ícone precisa ser uma classe do Bootstrap Icons, como 'bi-journal-text'.");

	/// <summary>Nome de exibição (coluna <c>ds_nome</c>).</summary>
	public string Nome { get; private set; }

	/// <summary>Identificador curto, único e imutável (coluna <c>ds_slug</c>).</summary>
	public string Slug { get; private set; }

	/// <summary>Classe do Bootstrap Icons exibida no menu (coluna <c>ds_icone</c>).</summary>
	public string Icone { get; private set; }

	/// <summary>Biblioteca ativa (coluna <c>fl_ativo</c>).</summary>
	public bool Ativo { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Renomeia a biblioteca.</summary>
	/// <param name="nome">Novo nome de exibição. Obrigatório.</param>
	/// <exception cref="DomainInvariantException">Se o nome for nulo ou vazio.</exception>
	public void Renomear(string nome)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Uma biblioteca exige nome não vazio.");
		}

		Nome = nome;
	}

	/// <summary>Troca o ícone exibido no menu.</summary>
	/// <param name="icone">Classe do Bootstrap Icons; vazio volta ao <see cref="IconePadrao"/>.</param>
	/// <exception cref="DomainInvariantException">Se o ícone não for uma classe do Bootstrap Icons.</exception>
	public void DefinirIcone(string? icone) => Icone = NormalizarIcone(icone);

	/// <summary>Desativa a biblioteca (deixa de aparecer no menu e de ser acessível).</summary>
	public void Desativar() => Ativo = false;

	/// <summary>Reativa a biblioteca.</summary>
	public void Ativar() => Ativo = true;
}
