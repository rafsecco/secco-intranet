using System.Security.Claims;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Application.Documentos;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Checagens de acesso a uma biblioteca de documentos: ler e escrever. Biblioteca inexistente,
/// inativa ou sem permissão dão a mesma resposta (nulo), para o controller devolver 404 indistinto
/// (ADR-0005).
/// </summary>
/// <param name="obterPorSlug">Leitura da biblioteca ativa pelo slug.</param>
/// <param name="listar">Listagem de bibliotecas.</param>
/// <param name="permissoes">Em quais bibliotecas o usuário tem leitura/escrita (ADR-0021).</param>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
/// <param name="environment">Ambiente de hospedagem — o modo aberto só vale em Development (ADR-0020).</param>
public sealed class AcessoABiblioteca(
	ObterBibliotecaPorSlugHandler obterPorSlug,
	ListarBibliotecasHandler listar,
	IPermissoesDeBiblioteca permissoes,
	IConfiguration configuration,
	IWebHostEnvironment environment)
{
	/// <summary>Se o modo aberto de DEV local está ativo (Development sem autenticação configurada).</summary>
	public bool ModoAbertoDeDev => AcessoAdministrativo.ModoAbertoDeDev(environment, configuration);

	/// <summary>A biblioteca, se ativa e legível pelo usuário; nulo caso contrário.</summary>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public Task<BibliotecaDto?> LeituraAsync(ClaimsPrincipal usuario, string slug, CancellationToken cancellationToken) =>
		ComAcaoAsync(usuario, slug, "read", cancellationToken);

	/// <summary>A biblioteca, se ativa e gravável pelo usuário; nulo caso contrário.</summary>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="slug">Slug da biblioteca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public Task<BibliotecaDto?> EscritaAsync(ClaimsPrincipal usuario, string slug, CancellationToken cancellationToken) =>
		ComAcaoAsync(usuario, slug, "write", cancellationToken);

	/// <summary>Donos de todas as bibliotecas ativas que o usuário pode ler.</summary>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlySet<ReferenciaDeDono>> DonosComLeituraAsync(
		ClaimsPrincipal usuario, CancellationToken cancellationToken)
	{
		var lista = await listar.HandleAsync(new ListarBibliotecasQuery(SomenteAtivas: true), cancellationToken).ConfigureAwait(false);

		if (lista.IsFailure)
		{
			return new HashSet<ReferenciaDeDono>();
		}

		var slugs = lista.Value.Select(b => b.Slug).ToList();
		IReadOnlySet<string> comLeitura = ModoAbertoDeDev
			? slugs.ToHashSet(StringComparer.OrdinalIgnoreCase)
			: await permissoes.SlugsComPermissaoAsync(usuario, "read", slugs, cancellationToken).ConfigureAwait(false);

		return slugs.Where(comLeitura.Contains).Select(ReferenciaDeDono.DaBiblioteca).ToHashSet();
	}

	private async Task<BibliotecaDto?> ComAcaoAsync(
		ClaimsPrincipal usuario, string slug, string acao, CancellationToken cancellationToken)
	{
		var biblioteca = await obterPorSlug.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (biblioteca.IsFailure)
		{
			return null;
		}

		if (ModoAbertoDeDev)
		{
			return biblioteca.Value;
		}

		var permitidos = await permissoes
			.SlugsComPermissaoAsync(usuario, acao, [biblioteca.Value.Slug], cancellationToken)
			.ConfigureAwait(false);

		return permitidos.Contains(biblioteca.Value.Slug) ? biblioteca.Value : null;
	}
}
