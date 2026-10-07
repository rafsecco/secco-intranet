using System.Text.RegularExpressions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Validação de entrada da área de tenants, checada antes de qualquer chamada à plataforma.</summary>
public static partial class RegrasDeTenant
{
	/// <summary>Tamanho máximo do nome (o da plataforma).</summary>
	public const int NomeMaxLength = 200;

	/// <summary>Tamanho máximo do slug (o da plataforma).</summary>
	public const int SlugMaxLength = 50;

	/// <summary>
	/// Kebab-case minúsculo, sem acento. Mais estrito que a plataforma (que só exige não vazio): o
	/// slug vira nome de banco e de login no provisionamento.
	/// </summary>
	/// <param name="slug">Slug a validar.</param>
	public static bool SlugValido(string? slug) =>
		slug is { Length: > 0 and <= SlugMaxLength } && PadraoDoSlug().IsMatch(slug);

	/// <summary>Valida os campos de criação.</summary>
	/// <param name="sistema">Sistema que o tenant representa.</param>
	/// <param name="responsavel">Responsável pelo sistema.</param>
	/// <param name="nome">Nome do tenant.</param>
	/// <param name="slug">Slug do tenant.</param>
	/// <returns>O primeiro erro, ou <c>null</c>.</returns>
	public static Error? ValidarCriacao(string? sistema, string? responsavel, string? nome, string? slug)
	{
		if (ValidarRegistro(sistema, responsavel) is { } erro)
		{
			return erro;
		}

		var nomeAparado = nome?.Trim() ?? string.Empty;

		if (nomeAparado.Length == 0)
		{
			return IntranetErrors.Tenants.NomeRequired;
		}

		if (nomeAparado.Length > NomeMaxLength)
		{
			return IntranetErrors.Tenants.NomeTooLong;
		}

		return SlugValido(slug?.Trim()) ? null : IntranetErrors.Tenants.SlugInvalido;
	}

	/// <summary>Valida o que vai para o cadastro local (criar e adotar).</summary>
	/// <param name="sistema">Sistema que o tenant representa.</param>
	/// <param name="responsavel">Responsável pelo sistema.</param>
	/// <returns>O primeiro erro, ou <c>null</c>.</returns>
	public static Error? ValidarRegistro(string? sistema, string? responsavel)
	{
		var s = sistema?.Trim() ?? string.Empty;
		var r = responsavel?.Trim() ?? string.Empty;

		if (s.Length == 0)
		{
			return IntranetErrors.Tenants.SistemaRequired;
		}

		if (s.Length > TenantAdministrado.SistemaMaxLength)
		{
			return IntranetErrors.Tenants.SistemaTooLong;
		}

		if (r.Length == 0)
		{
			return IntranetErrors.Tenants.ResponsavelRequired;
		}

		return r.Length > TenantAdministrado.ResponsavelMaxLength ? IntranetErrors.Tenants.ResponsavelTooLong : null;
	}

	/// <summary>Rótulo de quem age, o mesmo da trilha; <c>desconhecido</c> sem ator.</summary>
	/// <param name="ator">Ator atual.</param>
	public static string RotuloDoAtor(IAtorAtual ator)
	{
		ArgumentNullException.ThrowIfNull(ator);

		return ator.Atual()?.Nome is { Length: > 0 } nome ? nome : "desconhecido";
	}

	[GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
	private static partial Regex PadraoDoSlug();
}
