using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Pedido de arquivamento de um documento.</summary>
/// <param name="DocumentoId">Identificador do documento.</param>
/// <param name="DonosAdministrados">Setores e bibliotecas que o usuário administra.</param>
/// <param name="ExigirVinculo">
/// Quando <c>false</c>, dispensa a checagem — é o modo aberto de DEV, sem SecureGate para
/// emitir roles. Em produção é sempre <c>true</c>.
/// </param>
public sealed record ArquivarDocumentoCommand(
	Guid DocumentoId,
	IReadOnlySet<ReferenciaDeDono> DonosAdministrados,
	bool ExigirVinculo);

/// <summary>
/// Retira um documento de circulação. O registro e o arquivo cifrado permanecem: arquivar é
/// deixar de publicar, não apagar — quem precisar auditar o que já circulou ainda encontra.
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class ArquivarDocumentoHandler(IDocumentoRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de arquivamento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(
		ArquivarDocumentoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var encontrado = await repository.GetByIdAsync(command.DocumentoId, cancellationToken).ConfigureAwait(false);

		// Mesmo erro para inexistente e para fora do alcance, pelo mesmo motivo do download:
		// distinguir revelaria a existência do documento a quem não administra o dono.
		if (encontrado is null
			|| (command.ExigirVinculo && !command.DonosAdministrados.Contains(encontrado.Dono)))
		{
			return IntranetErrors.Documentos.NotFound;
		}

		if (!encontrado.Documento.Ativo)
		{
			// Já arquivado: repetir a operação não é erro nem gera escrita.
			return encontrado.Documento.Id;
		}

		if (await repository.TemFilhosAtivosAsync(encontrado.Documento.Id, cancellationToken).ConfigureAwait(false))
		{
			return IntranetErrors.Documentos.ItemComFilhos;
		}

		var irmaos = await repository
			.ListarIrmaosAsync(encontrado.Documento.Dono, encontrado.Documento.ParentId, cancellationToken)
			.ConfigureAwait(false);

		encontrado.Documento.Arquivar();

		PosicaoDeDocumentos.Renumerar(
			[.. irmaos.Where(item => item.Id != encontrado.Documento.Id)], encontrado.Documento.ParentId);

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.DocumentoArquivar,
					RecursosDeAuditoria.Documento,
					encontrado.Documento.Id.ToString(),
					TrilhaDeDono.Serializar(encontrado.Dono, [("titulo", encontrado.Documento.Titulo)], [])),
				cancellationToken)
			.ConfigureAwait(false);

		return encontrado.Documento.Id;
	}
}
