using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Comando de criação de um grupo de documentos em um setor.</summary>
/// <param name="SetorSlug">Slug do setor dono.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre. Opcional.</param>
/// <param name="CriadoPor">Identificação de quem cria.</param>
public sealed record CriarGrupoDeDocumentosCommand(
	string? SetorSlug,
	string? Titulo,
	string? Descricao,
	string CriadoPor);

/// <summary>
/// Caso de uso de criação de grupo: item sem arquivo que reúne documentos abaixo dele. Entra no
/// topo da raiz e os demais itens da raiz descem uma posição.
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
/// <param name="setorRepository">Consulta de setores.</param>
/// <param name="limites">Limites gerais de entrada do produto.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class CriarGrupoDeDocumentosHandler(
	IDocumentoRepository repository,
	ISetorRepository setorRepository,
	IntranetOptions limites,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<DocumentoDto>> HandleAsync(
		CriarGrupoDeDocumentosCommand command,
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

		var ordem = await PosicaoDeDocumentos
			.AbrirLugarAsync(repository, setor.Id, parentId: null, cancellationToken)
			.ConfigureAwait(false);

		var grupo = Documento.CriarGrupo(setor.Id, command.Titulo, command.Descricao, command.CriadoPor);
		grupo.Posicionar(null, ordem);

		await repository.AddAsync(grupo, cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DocumentoGrupoCriar,
					RecursosDeAuditoria.Documento,
					grupo.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						setor = setor.Slug,
						titulo = grupo.Titulo,
					})),
				cancellationToken)
			.ConfigureAwait(false);

		return new DocumentoDto(
			grupo.Id,
			grupo.Titulo,
			grupo.Descricao,
			grupo.NomeArquivo,
			grupo.ContentType,
			grupo.Tamanho,
			grupo.Visibilidade,
			setor.Id,
			setor.Nome,
			setor.Slug,
			grupo.CriadoPor,
			grupo.CreatedAt,
			grupo.Tipo,
			grupo.ParentId,
			grupo.Ordem);
	}
}
