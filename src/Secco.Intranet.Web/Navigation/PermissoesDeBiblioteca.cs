using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Web.Authentication;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Implementação real de <see cref="IPermissoesDeBiblioteca"/>, sobre <see cref="IAuthorizationService"/>
/// (mesma policy dinâmica do <see cref="ExigePermissaoAttribute"/>). Não existe permissão global de
/// biblioteca: o acesso é sempre por slug, e só o <c>intranet-admin</c> enxerga todas.
/// </summary>
/// <param name="authorizationService">Serviço de autorização do framework.</param>
public sealed class PermissoesDeBiblioteca(IAuthorizationService authorizationService) : IPermissoesDeBiblioteca
{
	/// <inheritdoc />
	public async Task<IReadOnlySet<string>> SlugsComPermissaoAsync(
		ClaimsPrincipal usuario, string acao, IReadOnlyList<string> slugs, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(usuario);
		ArgumentNullException.ThrowIfNull(slugs);

		if (AcessoAdministrativo.SomenteIntranetAdmin(usuario))
		{
			return slugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var slug in slugs)
		{
			if (await TemAsync(usuario, acao, slug))
			{
				resultado.Add(slug);
			}
		}

		return resultado;
	}

	private async Task<bool> TemAsync(ClaimsPrincipal usuario, string acao, string slug)
	{
		if ((await authorizationService.AuthorizeAsync(usuario, IntranetPermissoes.Biblioteca.Write(slug))).Succeeded)
		{
			return true;
		}

		return acao == "read"
			&& (await authorizationService.AuthorizeAsync(usuario, IntranetPermissoes.Biblioteca.Read(slug))).Succeeded;
	}
}
