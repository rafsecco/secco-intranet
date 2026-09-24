using System.Security.Claims;
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
/// "Meu perfil" passam por aqui. Quando o modelo de permissões chegar, é só esta classe que
/// troca "nome da Role" por "permissão" (<c>diretorio:read</c> / <c>diretorio:manage</c>).
/// </summary>
public static class AcessoAoDiretorio
{
	/// <summary>Role de administração do Diretório (Role fixa do produto).</summary>
	public const string RoleAdmin = "diretorio-admin";

	/// <summary>Role de uso do Diretório (Role fixa do produto).</summary>
	public const string RoleUsuario = "diretorio-user";

	/// <summary>
	/// Nível do usuário. <c>intranet-admin</c> e <c>diretorio-admin</c> dão
	/// <see cref="NivelDeAcessoAoDiretorio.Administrador"/>; <c>diretorio-user</c> dá
	/// <see cref="NivelDeAcessoAoDiretorio.Usuario"/>; qualquer outra Role — inclusive de setor e
	/// <c>inventario-admin</c> — dá <see cref="NivelDeAcessoAoDiretorio.Nenhum"/>.
	/// </summary>
	/// <param name="usuario">Usuário atual; <c>null</c> devolve <see cref="NivelDeAcessoAoDiretorio.Nenhum"/>.</param>
	public static NivelDeAcessoAoDiretorio Nivel(ClaimsPrincipal? usuario)
	{
		if (usuario is null)
		{
			return NivelDeAcessoAoDiretorio.Nenhum;
		}

		var nivel = NivelDeAcessoAoDiretorio.Nenhum;

		foreach (var claim in usuario.FindAll(SeccoClaims.Role))
		{
			if (Igual(claim.Value, AcessoAdministrativo.RoleIntranetAdmin) || Igual(claim.Value, RoleAdmin))
			{
				return NivelDeAcessoAoDiretorio.Administrador;
			}

			if (Igual(claim.Value, RoleUsuario))
			{
				nivel = NivelDeAcessoAoDiretorio.Usuario;
			}
		}

		return nivel;
	}

	/// <summary>Indica se o usuário tem pelo menos o nível informado.</summary>
	/// <param name="usuario">Usuário atual.</param>
	/// <param name="minimo">Nível mínimo exigido.</param>
	public static bool TemNivel(ClaimsPrincipal? usuario, NivelDeAcessoAoDiretorio minimo) =>
		Nivel(usuario) >= minimo && minimo != NivelDeAcessoAoDiretorio.Nenhum;

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

	private static bool Igual(string valor, string esperado) =>
		string.Equals(valor, esperado, StringComparison.OrdinalIgnoreCase);
}
