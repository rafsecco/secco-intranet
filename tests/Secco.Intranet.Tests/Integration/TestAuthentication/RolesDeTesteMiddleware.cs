using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Tests.Integration.TestAuthentication;

/// <summary>
/// Simula um usuário autenticado, lendo os headers <see cref="Header"/> (roles separadas por
/// vírgula) e <see cref="HeaderUsuario"/> (o claim <c>sub</c>, um Guid). Existe só nos testes: o
/// pipeline real nunca registra autenticação no ambiente <c>Testing</c> (ver
/// <c>IntranetWebFactory</c>), então esta é a única forma de exercitar autorização por role — e
/// "quem sou eu" — através do host HTTP real. Sem nenhum dos dois headers não faz nada.
/// </summary>
/// <param name="next">Próximo middleware do pipeline.</param>
public sealed class RolesDeTesteMiddleware(RequestDelegate next)
{
	/// <summary>Header lido: roles separadas por vírgula.</summary>
	public const string Header = "X-Test-Roles";

	/// <summary>Header lido: id do usuário (claim <c>sub</c>).</summary>
	public const string HeaderUsuario = "X-Test-User";

	/// <summary>Processa a requisição.</summary>
	/// <param name="context">Contexto HTTP da requisição atual.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var valorRoles = context.Request.Headers[Header].ToString();
		var valorUsuario = context.Request.Headers[HeaderUsuario].ToString();

		if (!string.IsNullOrWhiteSpace(valorRoles) || !string.IsNullOrWhiteSpace(valorUsuario))
		{
			var claims = new List<Claim>();

			foreach (var role in valorRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				claims.Add(new Claim(SeccoClaims.Role, role));
			}

			if (!string.IsNullOrWhiteSpace(valorUsuario))
			{
				claims.Add(new Claim(SeccoClaims.Subject, valorUsuario.Trim()));
			}

			context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Teste"));
		}

		await next(context).ConfigureAwait(false);
	}
}
