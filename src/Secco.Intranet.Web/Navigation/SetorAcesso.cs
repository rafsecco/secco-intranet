using System.Security.Claims;
using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Resolve a quais setores o usuário atual pertence a partir das Roles do SecureGate
/// (ADR-0001): <c>{slug}-admin</c> e <c>{slug}-user</c>. Não existe tabela de vínculo
/// usuário↔setor na Intranet — a fonte de verdade é a claim de role.
/// </summary>
public static class SetorAcesso
{
	/// <summary>Sufixo da Role de administração de um setor.</summary>
	public const string SufixoAdmin = "-admin";

	/// <summary>Sufixo da Role de leitura de um setor.</summary>
	public const string SufixoUsuario = "-user";

	/// <summary>Slugs dos setores aos quais o usuário está vinculado, em qualquer perfil.</summary>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve conjunto vazio.</param>
	public static IReadOnlySet<string> SlugsDoUsuario(ClaimsPrincipal? usuario)
	{
		var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (usuario is null)
		{
			return slugs;
		}

		foreach (var claim in usuario.FindAll(SeccoClaims.Role))
		{
			var slug = ExtrairSlug(claim.Value);

			if (slug is not null)
			{
				slugs.Add(slug);
			}
		}

		return slugs;
	}

	/// <summary>Slugs dos setores que o usuário administra (Role <c>{slug}-admin</c>).</summary>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve conjunto vazio.</param>
	public static IReadOnlySet<string> SlugsAdministrados(ClaimsPrincipal? usuario)
	{
		var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (usuario is null)
		{
			return slugs;
		}

		foreach (var claim in usuario.FindAll(SeccoClaims.Role))
		{
			if (claim.Value.EndsWith(SufixoAdmin, StringComparison.OrdinalIgnoreCase)
				&& claim.Value.Length > SufixoAdmin.Length)
			{
				slugs.Add(claim.Value[..^SufixoAdmin.Length]);
			}
		}

		return slugs;
	}

	/// <summary>Indica se o usuário administra ao menos um setor.</summary>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve <c>false</c>.</param>
	public static bool AdministraAlgumSetor(ClaimsPrincipal? usuario) =>
		usuario is not null
		&& usuario.FindAll(SeccoClaims.Role)
			.Any(claim => claim.Value.EndsWith(SufixoAdmin, StringComparison.OrdinalIgnoreCase)
				&& claim.Value.Length > SufixoAdmin.Length);

	/// <summary>Indica se o usuário administra o setor informado.</summary>
	/// <param name="usuario">Usuário atual.</param>
	/// <param name="slug">Slug do setor.</param>
	public static bool AdministraSetor(ClaimsPrincipal? usuario, string slug)
	{
		if (usuario is null || string.IsNullOrWhiteSpace(slug))
		{
			return false;
		}

		var role = slug + SufixoAdmin;

		return usuario.FindAll(SeccoClaims.Role)
			.Any(claim => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Filtra os setores que o usuário enxerga no menu e na navegação.
	/// </summary>
	/// <param name="setores">Setores do tenant atual.</param>
	/// <param name="usuario">Usuário atual.</param>
	/// <param name="exigirVinculo">
	/// Quando <c>false</c>, devolve todos os setores ativos — é o modo aberto de DEV, em que
	/// não há SecureGate para emitir roles. Em produção é sempre <c>true</c>.
	/// </param>
	public static IReadOnlyList<SetorDto> Visiveis(
		IReadOnlyList<SetorDto> setores,
		ClaimsPrincipal? usuario,
		bool exigirVinculo)
	{
		ArgumentNullException.ThrowIfNull(setores);

		var ativos = setores.Where(setor => setor.Ativo).ToList();

		if (!exigirVinculo)
		{
			return ativos;
		}

		var slugs = SlugsDoUsuario(usuario);

		return ativos.Where(setor => slugs.Contains(setor.Slug)).ToList();
	}

	private static string? ExtrairSlug(string role)
	{
		foreach (var sufixo in new[] { SufixoAdmin, SufixoUsuario })
		{
			if (role.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase) && role.Length > sufixo.Length)
			{
				return role[..^sufixo.Length];
			}
		}

		return null;
	}
}
