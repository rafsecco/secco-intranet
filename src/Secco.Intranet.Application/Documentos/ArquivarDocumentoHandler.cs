using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Pedido de arquivamento de um documento.</summary>
/// <param name="DocumentoId">Identificador do documento.</param>
/// <param name="SetoresAdministrados">Setores que o usuário administra (Role <c>{slug}-admin</c>).</param>
/// <param name="ExigirVinculo">
/// Quando <c>false</c>, dispensa a checagem — é o modo aberto de DEV, sem SecureGate para
/// emitir roles. Em produção é sempre <c>true</c>.
/// </param>
public sealed record ArquivarDocumentoCommand(
	Guid DocumentoId,
	IReadOnlySet<string> SetoresAdministrados,
	bool ExigirVinculo);

/// <summary>
/// Retira um documento de circulação. O registro e o arquivo cifrado permanecem: arquivar é
/// deixar de publicar, não apagar — quem precisar auditar o que já circulou ainda encontra.
/// </summary>
/// <param name="repository">Persistência de documentos.</param>
public sealed class ArquivarDocumentoHandler(IDocumentoRepository repository)
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
		// distinguir revelaria a existência do documento a quem não administra o setor.
		if (encontrado is null
			|| (command.ExigirVinculo && !command.SetoresAdministrados.Contains(encontrado.SetorSlug)))
		{
			return IntranetErrors.Documentos.NotFound;
		}

		if (!encontrado.Documento.Ativo)
		{
			// Já arquivado: repetir a operação não é erro nem gera escrita.
			return encontrado.Documento.Id;
		}

		encontrado.Documento.Arquivar();
		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return encontrado.Documento.Id;
	}
}
