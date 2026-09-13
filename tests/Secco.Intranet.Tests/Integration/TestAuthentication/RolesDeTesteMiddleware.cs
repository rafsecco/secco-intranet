using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Tests.Integration.TestAuthentication;

/// <summary>
/// Simula um usuário autenticado com roles específicas, lendo o header <see cref="Header"/>.
/// Existe só nos testes: o pipeline real nunca registra autenticação no ambiente
/// <c>Testing</c> (ver <c>IntranetWebFactory</c>), então esta é a única forma de exercitar
/// autorização por role através do host HTTP real. Sem o header, não faz nada — o
/// <c>ClaimsPrincipal</c> anônimo padrão segue intacto, e os testes que não usam isto
/// continuam se comportando exatamente como hoje.
/// </summary>
/// <param name="next">Próximo middleware do pipeline.</param>
public sealed class RolesDeTesteMiddleware(RequestDelegate next)
{
	/// <summary>Header lido: roles separadas por vírgula.</summary>
	public const string Header = "X-Test-Roles";

	/// <summary>Processa a requisição.</summary>
	/// <param name="context">Contexto HTTP da requisição atual.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var valor = context.Request.Headers[Header].ToString();

		if (!string.IsNullOrWhiteSpace(valor))
		{
			var roles = valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			var claims = roles.Select(role => new Claim(SeccoClaims.Role, role));
			context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Teste"));
		}

		await next(context).ConfigureAwait(false);
	}
}
