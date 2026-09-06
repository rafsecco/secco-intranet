using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Comando de publicação de um documento em um setor.</summary>
/// <param name="SetorSlug">Slug do setor dono.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre. Opcional.</param>
/// <param name="NomeArquivo">Nome original do arquivo enviado.</param>
/// <param name="Tamanho">Tamanho informado pelo transporte, em bytes.</param>
/// <param name="Conteudo">Fluxo do arquivo. Precisa ser posicionável para a apuração do tipo.</param>
/// <param name="Visibilidade">Quem enxerga o documento.</param>
/// <param name="CriadoPor">Identificação de quem publica.</param>
public sealed record PublicarDocumentoCommand(
	string? SetorSlug,
	string? Titulo,
	string? Descricao,
	string? NomeArquivo,
	long Tamanho,
	Stream Conteudo,
	Visibilidade Visibilidade,
	string CriadoPor);

/// <summary>
/// Caso de uso de publicação: valida o setor e o arquivo, grava o conteúdo cifrado e só
/// então persiste os metadados. Erros de negócio fluem por <see cref="Result{T}"/> (ADR-0004).
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
/// <param name="setorRepository">Consulta de setores.</param>
/// <param name="store">Armazenamento de arquivos.</param>
/// <param name="options">Limites de upload.</param>
/// <param name="limites">Limites gerais de entrada do produto.</param>
public sealed class PublicarDocumentoHandler(
	IDocumentoRepository repository,
	ISetorRepository setorRepository,
	IArquivoStore store,
	DocumentoOptions options,
	IntranetOptions limites)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<DocumentoDto>> HandleAsync(
		PublicarDocumentoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Titulo))
		{
			return IntranetErrors.Documentos.TituloRequired;
		}

		if (command.Titulo.Length > limites.MaxNameLength)
		{
			return IntranetErrors.Documentos.TituloTooLong(limites.MaxNameLength);
		}

		if (string.IsNullOrWhiteSpace(command.SetorSlug))
		{
			return IntranetErrors.Setores.NotFound;
		}

		var setor = await setorRepository.GetBySlugAsync(command.SetorSlug, cancellationToken).ConfigureAwait(false);

		if (setor is null || !setor.Ativo)
		{
			return IntranetErrors.Setores.NotFound;
		}

		if (command.Tamanho <= 0)
		{
			return IntranetErrors.Documentos.ArquivoRequired;
		}

		if (command.Tamanho > options.TamanhoMaximoBytes)
		{
			return IntranetErrors.Documentos.ArquivoMuitoGrande(options.TamanhoMaximoBytes);
		}

		var contentType = await ApurarContentTypeAsync(command, cancellationToken).ConfigureAwait(false);

		if (contentType is null)
		{
			return IntranetErrors.Documentos.ArquivoNaoAceito;
		}

		// O conteúdo vai para o armazenamento ANTES da linha existir. Um arquivo órfão é inerte
		// — cifrado, sem chave no banco e sem endereço conhecido por ninguém. A ordem inversa
		// produziria uma linha apontando para um arquivo inexistente, que é um documento quebrado
		// aparecendo na listagem.
		var gravado = await store.GravarAsync(command.Conteudo, cancellationToken).ConfigureAwait(false);

		var documento = new Documento(
			setor.Id,
			command.Titulo,
			command.Descricao,
			Path.GetFileName(command.NomeArquivo!),
			contentType,
			gravado.Tamanho,
			command.Visibilidade,
			gravado.CaminhoRelativo,
			gravado.ChaveEmbrulhada,
			command.CriadoPor);

		await repository.AddAsync(documento, cancellationToken).ConfigureAwait(false);

		return new DocumentoDto(
			documento.Id,
			documento.Titulo,
			documento.Descricao,
			documento.NomeArquivo,
			documento.ContentType,
			documento.Tamanho,
			documento.Visibilidade,
			setor.Id,
			setor.Nome,
			setor.Slug,
			documento.CriadoPor,
			documento.CreatedAt);
	}

	private async Task<string?> ApurarContentTypeAsync(
		PublicarDocumentoCommand command,
		CancellationToken cancellationToken)
	{
		var prefixo = new byte[TiposDeArquivo.BytesDeAssinatura];
		var lidos = await command.Conteudo.ReadAtLeastAsync(
			prefixo, prefixo.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

		if (command.Conteudo.CanSeek)
		{
			command.Conteudo.Seek(0, SeekOrigin.Begin);
		}

		return TiposDeArquivo.Apurar(command.NomeArquivo, prefixo.AsSpan(0, lidos), options.ExtensoesPermitidas);
	}
}
