using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Pedido de arquivamento.</summary>
/// <param name="PublicacaoId">Identificador da publicação.</param>
/// <param name="SetoresAdministrados">Setores que o usuário administra.</param>
/// <param name="ExigirVinculo">Quando <c>false</c>, dispensa a checagem — modo aberto de DEV.</param>
public sealed record ArquivarPublicacaoCommand(
	Guid PublicacaoId,
	IReadOnlySet<string> SetoresAdministrados,
	bool ExigirVinculo);

/// <summary>Tira uma publicação de circulação, preservando o registro.</summary>
/// <param name="repository">Persistência de publicações.</param>
public sealed class ArquivarPublicacaoHandler(IPublicacaoRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(
		ArquivarPublicacaoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var encontrada = await repository.GetByIdAsync(command.PublicacaoId, cancellationToken).ConfigureAwait(false);

		if (encontrada is null
			|| (command.ExigirVinculo && !command.SetoresAdministrados.Contains(encontrada.SetorSlug)))
		{
			return IntranetErrors.Publicacoes.NotFound;
		}

		if (!encontrada.Publicacao.Ativo)
		{
			return encontrada.Publicacao.Id;
		}

		encontrada.Publicacao.Arquivar();
		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return encontrada.Publicacao.Id;
	}
}
