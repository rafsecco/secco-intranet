using Secco.Intranet.Application.Diretorio;
using Secco.SDK.ClientCredentials;
using Secco.SecureGate.Client;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>Leitura das pessoas ativas do tenant pela API de diretório do SecureGate.</summary>
internal interface ILeitorDoDiretorio
{
	/// <summary>Lê todas as páginas e devolve só as pessoas ativas; lança em qualquer falha (sem lista parcial).</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<UsuarioParaDiretorio>> ListarAtivosAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Lê <c>GET /api/v1/directory/people</c> (scope <c>directory:read</c>) e traz os atributos do Entra.
/// Quem decide o fallback é o chamador: aqui qualquer falha lança, e a lista só é montada depois de
/// <b>todas</b> as páginas chegarem — nunca devolve parcial.
/// </summary>
/// <param name="httpClient">Client com <c>BaseAddress</c> e o handler de client credentials do escopo.</param>
internal sealed class LeitorDoDiretorioDoSecureGate(HttpClient httpClient) : ILeitorDoDiretorio
{
	/// <summary>Nome do <c>HttpClient</c> nomeado deste leitor.</summary>
	internal const string NomeDoHttpClient = "secco-intranet-directory";

	/// <summary>Scope pedido: só leitura do diretório (least privilege).</summary>
	internal const string Scope = "directory:read";

	private const string OrigemDiretorio = "directory";
	private const int TamanhoDaPagina = 100;

	/// <summary>Monta o handler de token do leitor, com o scope fixo <see cref="Scope"/>.</summary>
	/// <param name="baseUrl">URL base do SecureGate.</param>
	/// <param name="clientId">Id do client de produto.</param>
	/// <param name="clientSecret">Segredo do client.</param>
	/// <param name="store">Cache de token exclusivo deste leitor.</param>
	internal static SeccoClientCredentialsHandler CriarHandler(
		string baseUrl, string clientId, string clientSecret, SeccoAccessTokenStore store) =>
		new(baseUrl, clientId, clientSecret, Scope, store);

	/// <inheritdoc />
	public async Task<IReadOnlyList<UsuarioParaDiretorio>> ListarAtivosAsync(CancellationToken cancellationToken)
	{
		var client = new SecureGateClient(httpClient);
		List<DirectoryPersonDto> lidas = [];

		for (var pagina = 1; ; pagina++)
		{
			var resultado = await client.ListDirectoryPeopleAsync(pagina, TamanhoDaPagina, cancellationToken)
				.ConfigureAwait(false);

			lidas.AddRange(resultado.Items);

			if (resultado.Items.Count == 0 || (long)pagina * TamanhoDaPagina >= resultado.TotalCount)
			{
				break;
			}
		}

		return
		[
			.. lidas
				.Where(pessoa => pessoa.IsActive)
				.Select(pessoa => new UsuarioParaDiretorio(
					pessoa.Id, pessoa.Email ?? string.Empty, pessoa.DisplayName, Atributos(pessoa))),
		];
	}

	private static AtributosDoEntra? Atributos(DirectoryPersonDto pessoa) =>
		string.Equals(pessoa.AttributesSource, OrigemDiretorio, StringComparison.Ordinal)
			? new AtributosDoEntra(Aparar(pessoa.JobTitle), Aparar(pessoa.Department), Aparar(pessoa.BusinessPhone), pessoa.ManagerId)
			: null;

	private static string? Aparar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
