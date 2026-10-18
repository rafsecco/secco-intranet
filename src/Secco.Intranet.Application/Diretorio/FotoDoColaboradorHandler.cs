using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Referência da foto guardada, o que o endpoint de leitura precisa para servi-la.</summary>
/// <param name="CaminhoRelativo">Endereço da foto no armazenamento.</param>
/// <param name="ChaveEmbrulhada">Chave do arquivo cifrada pela chave mestra.</param>
/// <param name="ETag">Validador entre aspas, derivado do caminho sem expô-lo.</param>
/// <param name="ContentType">Tipo do conteúdo servido.</param>
public sealed record FotoGuardada(string CaminhoRelativo, string ChaveEmbrulhada, string ETag, string ContentType);

/// <summary>
/// Define, remove e consulta a foto de uma pessoa (quem pode é decisão do controller). A foto
/// nova é gravada antes do perfil apontar para ela, e a anterior só é apagada depois de salvo:
/// uma falha no meio deixa, no pior caso, um arquivo órfão, nunca um perfil sem foto.
/// </summary>
/// <param name="perfis">Perfis locais.</param>
/// <param name="imagens">Processamento da imagem enviada.</param>
/// <param name="arquivos">Armazenamento cifrado.</param>
/// <param name="trilha">Trilha de auditoria.</param>
/// <param name="logger">Log.</param>
public sealed class FotoDoColaboradorHandler(
	IPerfilColaboradorRepository perfis,
	IProcessadorDeImagem imagens,
	IArquivoStore arquivos,
	ITrilhaDeAuditoria trilha,
	ILogger<FotoDoColaboradorHandler> logger)
{
	private const string ContentTypeServido = "image/webp";

	/// <summary>Processa e guarda a foto enviada, substituindo a anterior.</summary>
	/// <param name="usuarioId">Dono da foto.</param>
	/// <param name="upload">Bytes enviados.</param>
	/// <param name="recorte">Recorte escolhido, ou nulo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> DefinirAsync(
		Guid usuarioId,
		Stream upload,
		RecorteDaFoto? recorte,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(upload);

		var processada = await imagens.ProcessarFotoAsync(upload, recorte, cancellationToken).ConfigureAwait(false);

		if (processada.IsFailure)
		{
			return Result.Failure(processada.Error);
		}

		ArquivoGravado gravado;

		using (var conteudo = new MemoryStream(processada.Value.Conteudo, writable: false))
		{
			gravado = await arquivos
				.GravarAsync(conteudo, PastasDeArquivo.FotosDoDiretorio, cancellationToken)
				.ConfigureAwait(false);
		}

		string? anterior = null;

		await EdicaoDePerfil.AplicarAsync(
			perfis,
			usuarioId,
			perfil =>
			{
				anterior = perfil.DefinirFoto(gravado.CaminhoRelativo, gravado.ChaveEmbrulhada);

				return ["foto"];
			},
			cancellationToken).ConfigureAwait(false);

		await RegistrarAsync(VerbosDeAuditoria.DiretorioFotoDefinir, usuarioId, cancellationToken).ConfigureAwait(false);
		await ApagarAsync(anterior, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}

	/// <summary>Tira a foto da pessoa. Sem foto, é sucesso e não há registro na trilha.</summary>
	/// <param name="usuarioId">Dono da foto.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> RemoverAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var perfil = await perfis.GetParaEdicaoAsync(usuarioId, cancellationToken).ConfigureAwait(false);
		var anterior = perfil?.RemoverFoto();

		if (anterior is null)
		{
			return Result.Success();
		}

		await perfis.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		await RegistrarAsync(VerbosDeAuditoria.DiretorioFotoRemover, usuarioId, cancellationToken).ConfigureAwait(false);
		await ApagarAsync(anterior, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}

	/// <summary>Devolve a referência da foto para o endpoint de leitura.</summary>
	/// <param name="usuarioId">Dono da foto.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<FotoGuardada>> ObterAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var perfil = await perfis.GetByUsuarioIdAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (perfil is not { FotoCaminhoRelativo: { } caminho, FotoChaveEmbrulhada: { } chave })
		{
			return Result.Failure<FotoGuardada>(IntranetErrors.Diretorio.FotoNaoEncontrada);
		}

		// Cada upload gera um caminho novo, então o validador muda a cada versão da foto.
		var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(caminho)))[..16];

		return Result.Success(new FotoGuardada(caminho, chave, $"\"{hash}\"", ContentTypeServido));
	}

	private async Task RegistrarAsync(string verbo, Guid usuarioId, CancellationToken cancellationToken) =>
		await trilha.RegistrarAsync(
			new RegistroDeAuditoria(
				verbo,
				RecursosDeAuditoria.Diretorio,
				usuarioId.ToString(),
				JsonSerializer.Serialize(new { usuarioId })),
			cancellationToken).ConfigureAwait(false);

	private async Task ApagarAsync(string? caminho, CancellationToken cancellationToken)
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
			// O perfil já aponta para outra foto (ou nenhuma): sobra só um arquivo órfão. Sem o caminho no log.
			logger.LogWarning(falha, "Não foi possível apagar uma foto antiga do Diretório; o arquivo ficou órfão.");
		}
	}
}
