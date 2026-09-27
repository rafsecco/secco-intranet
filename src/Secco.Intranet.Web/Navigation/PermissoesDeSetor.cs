using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Web.Authentication;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Implementação real de <see cref="IPermissoesDeSetor"/>, sobre <see cref="IAuthorizationService"/>
/// (mesma policy dinâmica do <see cref="ExigePermissaoAttribute"/>) — reaproveita o cache por
/// (tenant, role) do SDK, uma chamada por slug/global.
/// </summary>
/// <param name="authorizationService">Serviço de autorização do framework.</param>
public sealed class PermissoesDeSetor(IAuthorizationService authorizationService) : IPermissoesDeSetor
{
	/// <inheritdoc />
	public async Task<IReadOnlySet<string>> SlugsComPermissaoAsync(
		ClaimsPrincipal usuario, string acao, IReadOnlyList<string> setoresDoTenant, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(usuario);
		ArgumentNullException.ThrowIfNull(setoresDoTenant);

		if (AcessoAdministrativo.SomenteIntranetAdmin(usuario))
		{
			return setoresDoTenant.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		var global = $"setores:{acao}";
		var temGlobal = (await authorizationService.AuthorizeAsync(usuario, global)).Succeeded;

		if (temGlobal)
		{
			return setoresDoTenant.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var slug in setoresDoTenant)
		{
			var permissao = acao == "write" ? IntranetPermissoes.Setor.Write(slug) : IntranetPermissoes.Setor.Read(slug);
			var autorizado = await authorizationService.AuthorizeAsync(usuario, permissao);

			if (autorizado.Succeeded)
			{
				resultado.Add(slug);
			}
		}

		return resultado;
	}
}
