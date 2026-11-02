using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Comando de publicação de um documento em um setor ou numa biblioteca.</summary>
/// <param name="Dono">Referência ao dono: setor ou biblioteca.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre. Opcional.</param>
/// <param name="NomeArquivo">Nome original do arquivo enviado.</param>
/// <param name="Tamanho">Tamanho informado pelo transporte, em bytes.</param>
/// <param name="Conteudo">Fluxo do arquivo. Precisa ser posicionável para a apuração do tipo.</param>
/// <param name="Visibilidade">Quem enxerga o documento.</param>
/// <param name="CriadoPor">Identificação de quem publica.</param>
/// <param name="ParentId">Item da raiz sob o qual publicar; <c>null</c> publica na raiz.</param>
public sealed record PublicarDocumentoCommand(
	ReferenciaDeDono? Dono,
	string? Titulo,
	string? Descricao,
	string? NomeArquivo,
	long Tamanho,
	Stream Conteudo,
	Visibilidade Visibilidade,
	string CriadoPor,
	Guid? ParentId = null);

/// <summary>
/// Caso de uso de publicação: valida o dono e o arquivo, grava o conteúdo cifrado e só
/// então persiste os metadados. Erros de negócio fluem por <see cref="Result{T}"/> (ADR-0004).
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
/// <param name="resolvedor">Resolução do dono (setor ou biblioteca).</param>
/// <param name="store">Armazenamento de arquivos.</param>
/// <param name="options">Limites de upload.</param>
/// <param name="limites">Limites gerais de entrada do produto.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class PublicarDocumentoHandler(
	IDocumentoRepository repository,
	ResolvedorDeDono resolvedor,
	IArquivoStore store,
	DocumentoOptions options,
	IntranetOptions limites,
	ITrilhaDeAuditoria trilha)
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

		var dono = await resolvedor.ResolverAsync(command.Dono, cancellationToken).ConfigureAwait(false);

		if (dono is null)
		{
			return ResolvedorDeDono.NaoEncontrado(command.Dono);
		}

		if (command.Tamanho <= 0)
		{
			return IntranetErrors.Documentos.ArquivoRequired;
		}

		if (command.Tamanho > options.TamanhoMaximoBytes)
		{
			return IntranetErrors.Documentos.ArquivoMuitoGrande(options.TamanhoMaximoBytes);
		}

		var contentType = await TiposDeArquivo
			.ApurarAsync(command.Conteudo, command.NomeArquivo, options.ExtensoesPermitidas, cancellationToken)
			.ConfigureAwait(false);

		if (contentType is null)
		{
			return IntranetErrors.Documentos.ArquivoNaoAceito;
		}

		// O pai é validado antes de gravar o conteúdo: recusar depois deixaria um arquivo órfão.
		if (command.ParentId is { } parentId)
		{
			var pai = await repository.GetByIdAsync(parentId, cancellationToken).ConfigureAwait(false);

			if (!PosicaoDeDocumentos.PodeSerPai(pai, dono.Referencia))
			{
				return IntranetErrors.Documentos.ParentInvalido;
			}
		}

		// O conteúdo vai para o armazenamento ANTES da linha existir. Um arquivo órfão é inerte
		// — cifrado, sem chave no banco e sem endereço conhecido por ninguém. A ordem inversa
		// produziria uma linha apontando para um arquivo inexistente, que é um documento quebrado
		// aparecendo na listagem.
		var gravado = await store.GravarAsync(command.Conteudo, cancellationToken).ConfigureAwait(false);

		var ordem = await PosicaoDeDocumentos
			.AbrirLugarAsync(repository, dono.Dono, command.ParentId, cancellationToken)
			.ConfigureAwait(false);

		var documento = new Documento(
			dono.Dono,
			command.Titulo,
			command.Descricao,
			TiposDeArquivo.NomeSemCaminho(command.NomeArquivo!),
			contentType,
			gravado.Tamanho,
			// Documento de biblioteca grava sempre Setor: a visibilidade só existe para o setor.
			dono.Referencia.Tipo == TipoDeDono.Biblioteca ? Visibilidade.Setor : command.Visibilidade,
			gravado.CaminhoRelativo,
			gravado.ChaveEmbrulhada,
			command.CriadoPor,
			command.ParentId,
			ordem);

		await repository.AddAsync(documento, cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DocumentoPublicar,
					RecursosDeAuditoria.Documento,
					documento.Id.ToString(),
					TrilhaDeDono.Serializar(
						dono.Referencia,
						[
							("titulo", documento.Titulo),
							("arquivo", documento.NomeArquivo),
							("visibilidade", documento.Visibilidade.ToString()),
							("tamanho", documento.Tamanho),
						],
						[("pai", documento.ParentId?.ToString())])),
				cancellationToken)
			.ConfigureAwait(false);

		return new DocumentoDto(
			documento.Id,
			documento.Titulo,
			documento.Descricao,
			documento.NomeArquivo,
			documento.ContentType,
			documento.Tamanho,
			documento.Visibilidade,
			dono.Referencia,
			dono.Nome,
			documento.CriadoPor,
			documento.CreatedAt,
			documento.Tipo,
			documento.ParentId,
			documento.Ordem);
	}
}
