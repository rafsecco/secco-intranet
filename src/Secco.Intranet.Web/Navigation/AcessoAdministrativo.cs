using System.Security.Claims;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Checa Roles fixas (não derivadas de slug de setor) — o modelo de autorização do
/// Inventário e da futura Área administrativa (ADR-0008). Diferente de
/// <see cref="SetorAcesso"/>, que deriva slug de um sufixo <c>-admin</c>/<c>-user</c>; aqui a
/// Role é um nome exato, e <see cref="RoleIntranetAdmin"/> sempre concede acesso, qualquer que
/// seja a Role específica pedida — é o superusuário da instalação.
/// </summary>
public static class AcessoAdministrativo
{
	/// <summary>Role do superusuário da instalação — sempre tem acesso a tudo (ADR-0008).</summary>
	public const string RoleIntranetAdmin = "intranet-admin";

	/// <summary>Role do administrador do recurso Inventário (ver docs/specs/2026-09-13-inventario-design.md).</summary>
	public const string RoleInventarioAdmin = "inventario-admin";

	/// <summary>
	/// Indica se o usuário administra o recurso: tem <see cref="RoleIntranetAdmin"/> OU a Role
	/// específica informada.
	/// </summary>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve <c>false</c>.</param>
	/// <param name="roleEspecifica">Role fixa do recurso (ex.: <c>inventario-admin</c>).</param>
	public static bool TemAcesso(ClaimsPrincipal? usuario, string roleEspecifica)
	{
		if (usuario is null)
		{
			return false;
		}

		return usuario.FindAll(SeccoClaims.Role).Any(claim =>
			string.Equals(claim.Value, RoleIntranetAdmin, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(claim.Value, roleEspecifica, StringComparison.OrdinalIgnoreCase));
	}
}
