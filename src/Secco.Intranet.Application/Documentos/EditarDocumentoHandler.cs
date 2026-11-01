using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Pedido de edição de um documento: metadados e, opcionalmente, troca do arquivo.</summary>
/// <param name="DocumentoId">Identificador do documento.</param>
/// <param name="SetorSlug">Slug do setor da rota; precisa ser o do dono do documento.</param>
/// <param name="Titulo">Novo título.</param>
/// <param name="Descricao">Nova descrição; vazia limpa.</param>
/// <param name="Visibilidade">Nova visibilidade. Ignorada em grupo, que é sempre do setor.</param>
/// <param name="NomeArquivo">Nome original do novo arquivo, quando há troca.</param>
/// <param name="Tamanho">Tamanho informado pelo transporte, em bytes.</param>
/// <param name="Conteudo">Fluxo do novo arquivo; <c>null</c> mantém o arquivo atual.</param>
/// <param name="SetoresAdministrados">Setores que o usuário administra (Role <c>{slug}-admin</c>).</param>
/// <param name="ExigirVinculo">
/// Quando <c>false</c>, dispensa a checagem de vínculo — modo aberto de DEV. Em produção é sempre <c>true</c>.
/// </param>
public sealed record EditarDocumentoCommand(
	Guid DocumentoId,
	string SetorSlug,
	string? Titulo,
	string? Descricao,
	Visibilidade Visibilidade,
	string? NomeArquivo,
	long Tamanho,
	Stream? Conteudo,
	IReadOnlySet<string> SetoresAdministrados,
	bool ExigirVinculo);

/// <summary>
/// Edita um documento. O arquivo novo é gravado antes da linha mudar e o antigo só é removido
/// depois do salvamento: a falha em qualquer ponto deixa, no pior caso, um arquivo órfão e
/// inerte — nunca um documento apontando para um arquivo que não existe.
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
/// <param name="store">Armazenamento de arquivos.</param>
/// <param name="options">Limites de upload.</param>
/// <param name="limites">Limites gerais de entrada do produto.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="logger">Registro de diagnóstico.</param>
public sealed class EditarDocumentoHandler(
	IDocumentoRepository repository,
	IArquivoStore store,
	DocumentoOptions options,
	IntranetOptions limites,
	ITrilhaDeAuditoria trilha,
	ILogger<EditarDocumentoHandler> logger)
{
	private static readonly JsonSerializerOptions OpcoesDaTrilha = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de edição.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(
		EditarDocumentoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var encontrado = await repository.GetByIdAsync(command.DocumentoId, cancellationToken).ConfigureAwait(false);

		if (!ObterDocumentoParaEdicaoHandler.Alcanca(
			encontrado, command.SetorSlug, command.SetoresAdministrados, command.ExigirVinculo))
		{
			return IntranetErrors.Documentos.NotFound;
		}

		var documento = encontrado!.Documento;

		if (string.IsNullOrWhiteSpace(command.Titulo))
		{
			return IntranetErrors.Documentos.TituloRequired;
		}

		if (command.Titulo.Length > limites.MaxNameLength)
		{
			return IntranetErrors.Documentos.TituloTooLong(limites.MaxNameLength);
		}

		if (command.Descricao is { } descricaoBruta && descricaoBruta.Length > limites.MaxDescriptionLength)
		{
			return IntranetErrors.Documentos.DescricaoTooLong(limites.MaxDescriptionLength);
		}

		string? contentType = null;

		if (command.Conteudo is not null)
		{
			if (documento.EhGrupo)
			{
				return IntranetErrors.Documentos.ArquivoEmGrupo;
			}

			if (command.Tamanho <= 0)
			{
				return IntranetErrors.Documentos.ArquivoRequired;
			}

			if (command.Tamanho > options.TamanhoMaximoBytes)
			{
				return IntranetErrors.Documentos.ArquivoMuitoGrande(options.TamanhoMaximoBytes);
			}

			contentType = await TiposDeArquivo
				.ApurarAsync(command.Conteudo, command.NomeArquivo, options.ExtensoesPermitidas, cancellationToken)
				.ConfigureAwait(false);

			if (contentType is null)
			{
				return IntranetErrors.Documentos.ArquivoNaoAceito;
			}
		}

		var tituloAntes = documento.Titulo;
		var descricaoAntes = documento.Descricao;
		var visibilidadeAntes = documento.Visibilidade;
		var arquivoAntes = documento.EhGrupo ? null : new ArquivoNaTrilha(documento.NomeArquivo, documento.Tamanho);

		// O conteúdo vai para o armazenamento ANTES da linha mudar: um arquivo órfão é inerte,
		// enquanto a ordem inversa deixaria um documento apontando para um arquivo inexistente.
		ArquivoGravado? gravado = null;

		if (command.Conteudo is not null)
		{
			gravado = await store.GravarAsync(command.Conteudo, cancellationToken).ConfigureAwait(false);
		}

		documento.Editar(command.Titulo, command.Descricao);

		if (!documento.EhGrupo)
		{
			documento.AlterarVisibilidade(command.Visibilidade);
		}

		string? caminhoAnterior = null;

		if (gravado is not null)
		{
			caminhoAnterior = documento.TrocarArquivo(
				Path.GetFileName(command.NomeArquivo!),
				contentType!,
				gravado.Tamanho,
				gravado.CaminhoRelativo,
				gravado.ChaveEmbrulhada);
		}

		var tituloMudou = documento.Titulo != tituloAntes;
		var descricaoMudou = documento.Descricao != descricaoAntes;
		var visibilidadeMudou = documento.Visibilidade != visibilidadeAntes;

		if (!tituloMudou && !descricaoMudou && !visibilidadeMudou && gravado is null)
		{
			return documento.Id;
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var detalhes = new DetalhesDaEdicao(
			encontrado.SetorSlug,
			tituloMudou ? new Mudanca<string>(tituloAntes, documento.Titulo) : null,
			visibilidadeMudou
				? new Mudanca<string>(visibilidadeAntes.ToString(), documento.Visibilidade.ToString())
				: null,
			descricaoMudou ? true : null,
			gravado is null
				? null
				: new Mudanca<ArquivoNaTrilha>(arquivoAntes!, new ArquivoNaTrilha(documento.NomeArquivo, documento.Tamanho)));

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DocumentoEditar,
					RecursosDeAuditoria.Documento,
					documento.Id.ToString(),
					JsonSerializer.Serialize(detalhes, OpcoesDaTrilha)),
				cancellationToken)
			.ConfigureAwait(false);

		if (caminhoAnterior is not null)
		{
			await RemoverAntigoAsync(caminhoAnterior, cancellationToken).ConfigureAwait(false);
		}

		return documento.Id;
	}

	private async Task RemoverAntigoAsync(string caminho, CancellationToken cancellationToken)
	{
		try
		{
			await store.RemoverAsync(caminho, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception falha) when (falha is not OperationCanceledException)
		{
			// O documento já aponta para o arquivo novo: sobra só um arquivo órfão. Sem o caminho no log.
			// Só o tipo da exceção: a mensagem de falhas de I/O costuma trazer o caminho absoluto.
			logger.LogWarning(
				"Não foi possível apagar o arquivo anterior de um documento ({TipoDaFalha}); o arquivo ficou órfão.",
				falha.GetType().Name);
		}
	}

	private sealed record Mudanca<T>(
		[property: JsonPropertyName("de")] T De,
		[property: JsonPropertyName("para")] T Para);

	private sealed record ArquivoNaTrilha(
		[property: JsonPropertyName("nome")] string? Nome,
		[property: JsonPropertyName("tamanho")] long? Tamanho);

	private sealed record DetalhesDaEdicao(
		[property: JsonPropertyName("setor")] string Setor,
		[property: JsonPropertyName("titulo")] Mudanca<string>? Titulo,
		[property: JsonPropertyName("visibilidade")] Mudanca<string>? Visibilidade,
		[property: JsonPropertyName("descricaoAlterada")] bool? DescricaoAlterada,
		[property: JsonPropertyName("arquivo")] Mudanca<ArquivoNaTrilha>? Arquivo);
}
