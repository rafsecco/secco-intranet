using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Navigation;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Entrega de documentos. É o <b>único</b> caminho até o conteúdo: os arquivos não são
/// servidos como conteúdo estático nem têm endereço adivinhável, porque a visibilidade
/// (setor ou empresa) precisa ser avaliada a cada leitura.
/// </summary>
/// <param name="handler">Caso de uso de download.</param>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
/// <param name="searchSetores">Busca de setores, para saber o universo de slugs a checar.</param>
/// <param name="permissoesDeSetor">Em quais setores o usuário tem leitura (ADR-0021).</param>
[Route("documentos")]
public sealed class DocumentosController(
	BaixarDocumentoHandler handler,
	IConfiguration configuration,
	SearchSetoresHandler searchSetores,
	IPermissoesDeSetor permissoesDeSetor) : Controller
{
	private const int LimiteSetoresConsultados = 200;

	/// <summary>Entrega o conteúdo decifrado de um documento.</summary>
	/// <param name="id">Identificador do documento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet("{id:guid}/download")]
	public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken = default)
	{
		var query = new BaixarDocumentoQuery(
			id,
			await SlugsComLeituraAsync(cancellationToken).ConfigureAwait(false),
			ExigirVinculo: IntranetAuthenticationExtensions.IsConfigured(configuration));

		var resultado = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

		if (resultado.IsFailure)
		{
			return NotFound();
		}

		var documento = resultado.Value;

		// FileNameStar usa RFC 5987, que preserva acento no nome do arquivo baixado.
		var disposicao = new ContentDispositionHeaderValue("attachment")
		{
			FileNameStar = documento.NomeArquivo,
		};

		Response.Headers.ContentDisposition = disposicao.ToString();
		Response.Headers.XContentTypeOptions = "nosniff";
		Response.Headers.CacheControl = "private, no-store";
		Response.ContentType = documento.ContentType;
		Response.ContentLength = documento.Tamanho;

		await documento.EscreverConteudo(Response.Body, cancellationToken).ConfigureAwait(false);

		return new EmptyResult();
	}

	/// <summary>
	/// Setores em que o usuário tem leitura — vazio sem autenticação configurada (modo aberto de
	/// DEV/Testing), quando o handler ignora o filtro por completo (<c>ExigirVinculo: false</c>).
	/// </summary>
	private async Task<IReadOnlySet<string>> SlugsComLeituraAsync(CancellationToken cancellationToken)
	{
		if (!IntranetAuthenticationExtensions.IsConfigured(configuration))
		{
			return new HashSet<string>();
		}

		var setores = await searchSetores
			.HandleAsync(new SetorSearchCriteria(ApenasAtivos: true, Page: new PageRequest(1, LimiteSetoresConsultados)), cancellationToken)
			.ConfigureAwait(false);

		if (setores.IsFailure)
		{
			return new HashSet<string>();
		}

		var slugs = setores.Value.Items.Select(s => s.Slug).ToList();

		return await permissoesDeSetor.SlugsComPermissaoAsync(User, "read", slugs, cancellationToken).ConfigureAwait(false);
	}
}
