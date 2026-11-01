using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Arquivo de imagem enviado junto com a publicação. Quem abriu o fluxo é quem o fecha.</summary>
/// <param name="Conteudo">Bytes enviados.</param>
public sealed record ImagemEnviada(Stream Conteudo);

/// <summary>
/// Passos de armazenamento da imagem, compartilhados por publicar e editar para as duas
/// operações não divergirem na ordem (gravar, salvar, só então apagar o anterior).
/// </summary>
internal static class ImagemDaPublicacao
{
	/// <summary>Mensagem do aviso quando o texto da publicação não tem o que resumir.</summary>
	internal const string ResumoPadrao = "Abra a publicação na intranet para ver.";

	/// <summary>Erro quando a descrição passa do limite, ou <c>null</c>.</summary>
	/// <param name="descricao">Descrição informada.</param>
	internal static Error? ValidarDescricao(string? descricao) =>
		descricao?.Trim().Length > Publicacao.LimiteDaDescricaoDaImagem
			? IntranetErrors.Publicacoes.ImagemDescricaoTooLong
			: null;

	/// <summary>Processa a imagem e a grava na pasta do mural; falha do arquivo volta como <see cref="Result"/>.</summary>
	/// <param name="imagem">Arquivo enviado.</param>
	/// <param name="processador">Porta de processamento.</param>
	/// <param name="arquivos">Armazenamento cifrado.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	internal static async Task<Result<ArquivoGravado>> GravarAsync(
		ImagemEnviada imagem,
		IProcessadorDeImagem processador,
		IArquivoStore arquivos,
		CancellationToken cancellationToken)
	{
		var processada = await processador
			.ProcessarImagemDePublicacaoAsync(imagem.Conteudo, cancellationToken)
			.ConfigureAwait(false);

		if (processada.IsFailure)
		{
			return Result.Failure<ArquivoGravado>(processada.Error);
		}

		using var conteudo = new MemoryStream(processada.Value.Conteudo, writable: false);

		return Result.Success(await arquivos
			.GravarAsync(conteudo, PastasDeArquivo.ImagensDoMural, cancellationToken)
			.ConfigureAwait(false));
	}

	/// <summary>
	/// Executa a persistência e, se qualquer passo falhar, apaga o arquivo recém-guardado
	/// (melhor esforço) e relança: sem a linha, o arquivo novo não tem a quem servir.
	/// </summary>
	/// <typeparam name="T">Resultado da persistência.</typeparam>
	/// <param name="persistir">Passos que aplicam a imagem ao domínio e salvam.</param>
	/// <param name="caminhoNovo">Caminho do arquivo recém-guardado, ou nulo se não houve.</param>
	/// <param name="arquivos">Armazenamento cifrado.</param>
	/// <param name="logger">Log.</param>
	internal static async Task<T> PersistirOuDescartarAsync<T>(
		Func<Task<T>> persistir,
		string? caminhoNovo,
		IArquivoStore arquivos,
		ILogger logger)
	{
		try
		{
			return await persistir().ConfigureAwait(false);
		}
		catch
		{
			await ApagarAsync(caminhoNovo, arquivos, logger, CancellationToken.None).ConfigureAwait(false);

			throw;
		}
	}

	/// <summary>Apaga o arquivo anterior; falha vira aviso no log, nunca erro.</summary>
	/// <param name="caminho">Caminho anterior, ou nulo.</param>
	/// <param name="arquivos">Armazenamento cifrado.</param>
	/// <param name="logger">Log.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	internal static async Task ApagarAsync(
		string? caminho,
		IArquivoStore arquivos,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		if (caminho is null)
		{
			return;
		}

		try
		{
			await arquivos.RemoverAsync(caminho, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception falha) when (falha is not OperationCanceledException)
		{
			// A publicação já aponta para outra imagem (ou nenhuma): sobra só um arquivo órfão.
			// Só o tipo da exceção: a mensagem de falhas de I/O costuma trazer o caminho absoluto.
			logger.LogWarning("Não foi possível apagar uma imagem antiga do Mural ({TipoDaFalha}); o arquivo ficou órfão.", falha.GetType().Name);
		}
	}
}
