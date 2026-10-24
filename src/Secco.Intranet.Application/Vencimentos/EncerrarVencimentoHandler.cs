using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Pedido de encerramento de um vencimento.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="SetorId">Setor da rota; item de outro setor não é encontrado.</param>
/// <param name="SetorSlug">Slug do setor.</param>
public sealed record EncerrarVencimentoCommand(Guid Id, Guid SetorId, string SetorSlug);

/// <summary>Encerra um vencimento: deixa de ser acompanhado e de gerar avisos.</summary>
public sealed class EncerrarVencimentoHandler(
	IItemVencimentoRepository repository,
	RelogioDeVencimentos relogio,
	IAtorAtual ator,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de encerramento.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(EncerrarVencimentoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var item = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (item is null || item.SetorId != command.SetorId)
		{
			return IntranetErrors.Vencimentos.NotFound;
		}

		if (item.EstaEncerrado)
		{
			// Já encerrado: repetir não é erro, não escreve e não vai à trilha.
			return item.Id;
		}

		item.Encerrar(ator.Atual()?.Nome ?? ApoioDeVencimentos.AutorDoSistema, relogio.Agora());
		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.VencimentoEncerrar,
					RecursosDeAuditoria.Vencimento,
					item.Id.ToString(),
					ApoioDeVencimentos.Metadata(item, command.SetorSlug)),
				cancellationToken)
			.ConfigureAwait(false);

		return item.Id;
	}
}
