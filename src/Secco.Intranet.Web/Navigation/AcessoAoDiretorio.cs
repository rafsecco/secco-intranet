using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Nível de acesso de um usuário ao Diretório organizacional.</summary>
public enum NivelDeAcessoAoDiretorio
{
	/// <summary>Sem acesso: 403 em toda rota, sem item de menu.</summary>
	Nenhum = 0,

	/// <summary>Vê o diretório e edita o próprio contato.</summary>
	Usuario = 1,

	/// <summary>Edita todos os campos de todos e importa CSV.</summary>
	Administrador = 2,
}

/// <summary>
/// Única função que decide o nível de acesso ao Diretório: gate das rotas, item de menu e link
/// "Meu perfil" passam por aqui. Resolve por permissão (ADR-0021: <c>diretorio:read</c> /
/// <c>diretorio:manage</c>), com <c>intranet-admin</c> sempre <see cref="NivelDeAcessoAoDiretorio.Administrador"/>
/// por identidade (ADR-0008) — nunca precisa ganhar a permissão gravada.
/// </summary>
public static class AcessoAoDiretorio
{
	/// <summary>
	/// Nível do usuário. <c>diretorio:manage</c> dá <see cref="NivelDeAcessoAoDiretorio.Administrador"/>;
	/// só <c>diretorio:read</c> dá <see cref="NivelDeAcessoAoDiretorio.Usuario"/>; sem nenhuma das
	/// duas — e sem ser <c>intranet-admin</c> — dá <see cref="NivelDeAcessoAoDiretorio.Nenhum"/>.
	/// </summary>
	/// <param name="authorizationService">Serviço de autorização do framework.</param>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve <see cref="NivelDeAcessoAoDiretorio.Nenhum"/>.</param>
	public static async Task<NivelDeAcessoAoDiretorio> NivelAsync(IAuthorizationService authorizationService, ClaimsPrincipal? usuario)
	{
		if (usuario is null)
		{
			return NivelDeAcessoAoDiretorio.Nenhum;
		}

		if (AcessoAdministrativo.SomenteIntranetAdmin(usuario))
		{
			return NivelDeAcessoAoDiretorio.Administrador;
		}

		if ((await authorizationService.AuthorizeAsync(usuario, IntranetPermissoes.Diretorio.Manage)).Succeeded)
		{
			return NivelDeAcessoAoDiretorio.Administrador;
		}

		if ((await authorizationService.AuthorizeAsync(usuario, IntranetPermissoes.Diretorio.Read)).Succeeded)
		{
			return NivelDeAcessoAoDiretorio.Usuario;
		}

		return NivelDeAcessoAoDiretorio.Nenhum;
	}

	/// <summary>Indica se o usuário tem pelo menos o nível informado.</summary>
	/// <param name="authorizationService">Serviço de autorização do framework.</param>
	/// <param name="usuario">Usuário atual.</param>
	/// <param name="minimo">Nível mínimo exigido.</param>
	public static async Task<bool> TemNivelAsync(IAuthorizationService authorizationService, ClaimsPrincipal? usuario, NivelDeAcessoAoDiretorio minimo)
	{
		var nivel = await NivelAsync(authorizationService, usuario).ConfigureAwait(false);

		return nivel >= minimo && minimo != NivelDeAcessoAoDiretorio.Nenhum;
	}

	/// <summary>
	/// Id do usuário logado no SecureGate, lido do claim <c>sub</c>. É a **única** fonte de "quem
	/// sou eu": nunca se aceita um id vindo do formulário para isso.
	/// </summary>
	/// <param name="usuario">Usuário atual.</param>
	public static Guid? UsuarioId(ClaimsPrincipal? usuario)
	{
		var sub = usuario?.FindFirst(SeccoClaims.Subject)?.Value;

		return Guid.TryParse(sub, out var id) && id != Guid.Empty ? id : null;
	}
}
