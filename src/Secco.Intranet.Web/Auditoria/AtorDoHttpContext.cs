using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.Auditoria;

/// <summary>
/// Resolve o ator a partir do usuário da requisição. Fica no Web porque é o único lugar que
/// conhece <c>HttpContext</c> — a Application só enxerga a porta.
/// </summary>
/// <param name="httpContextAccessor">Acesso à requisição atual.</param>
public sealed class AtorDoHttpContext(IHttpContextAccessor httpContextAccessor) : IAtorAtual
{
	/// <inheritdoc />
	public AtorDaAcao? Atual()
	{
		var usuario = httpContextAccessor.HttpContext?.User;

		if (usuario?.Identity?.IsAuthenticated != true)
		{
			return null;
		}

		var id = usuario.FindFirst(SeccoClaims.Subject)?.Value;

		return string.IsNullOrWhiteSpace(id)
			? null
			: new AtorDaAcao(id, usuario.Identity.Name ?? id);
	}
}
