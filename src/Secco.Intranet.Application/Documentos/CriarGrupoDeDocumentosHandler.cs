using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Comando de criação de um grupo de documentos em um setor ou numa biblioteca.</summary>
/// <param name="Dono">Referência ao dono: setor ou biblioteca.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre. Opcional.</param>
/// <param name="CriadoPor">Identificação de quem cria.</param>
public sealed record CriarGrupoDeDocumentosCommand(
	ReferenciaDeDono? Dono,
	string? Titulo,
	string? Descricao,
	string CriadoPor);

/// <summary>
/// Caso de uso de criação de grupo: item sem arquivo que reúne documentos abaixo dele. Entra no
/// topo da raiz e os demais itens da raiz descem uma posição.
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
/// <param name="resolvedor">Resolução do dono (setor ou biblioteca).</param>
/// <param name="limites">Limites gerais de entrada do produto.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class CriarGrupoDeDocumentosHandler(
	IDocumentoRepository repository,
	ResolvedorDeDono resolvedor,
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

		var dono = await resolvedor.ResolverAsync(command.Dono, cancellationToken).ConfigureAwait(false);

		if (dono is null)
		{
			return ResolvedorDeDono.NaoEncontrado(command.Dono);
		}

		var ordem = await PosicaoDeDocumentos
			.AbrirLugarAsync(repository, dono.Dono, parentId: null, cancellationToken)
			.ConfigureAwait(false);

		var grupo = Documento.CriarGrupo(dono.Dono, command.Titulo, command.Descricao, command.CriadoPor);
		grupo.Posicionar(null, ordem);

		await repository.AddAsync(grupo, cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DocumentoGrupoCriar,
					RecursosDeAuditoria.Documento,
					grupo.Id.ToString(),
					TrilhaDeDono.Serializar(dono.Referencia, [], [("titulo", grupo.Titulo)])),
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
			dono.Referencia,
			dono.Nome,
			grupo.CriadoPor,
			grupo.CreatedAt,
			grupo.Tipo,
			grupo.ParentId,
			grupo.Ordem);
	}
}
