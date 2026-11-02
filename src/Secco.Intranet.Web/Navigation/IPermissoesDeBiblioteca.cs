using System.Security.Claims;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Em quais bibliotecas o usuário tem uma dada ação, por permissão (ADR-0021).</summary>
public interface IPermissoesDeBiblioteca
{
	/// <summary>Slugs, dentre os informados, em que o usuário tem a ação pedida.</summary>
	/// <param name="usuario">Usuário atual.</param>
	/// <param name="acao"><c>"read"</c> ou <c>"write"</c> — sem o prefixo de recurso. Em <c>"read"</c>, ter <c>write</c> também conta.</param>
	/// <param name="slugs">Slugs a considerar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlySet<string>> SlugsComPermissaoAsync(
		ClaimsPrincipal usuario, string acao, IReadOnlyList<string> slugs, CancellationToken cancellationToken = default);
}
