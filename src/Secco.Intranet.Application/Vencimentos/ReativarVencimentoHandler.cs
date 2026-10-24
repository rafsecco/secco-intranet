using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Exceptions;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Pedido de reativação de um vencimento encerrado.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="SetorId">Setor da rota; item de outro setor não é encontrado.</param>
/// <param name="SetorSlug">Slug do setor.</param>
/// <param name="NovaData">Nova data de vencimento; deve ser posterior a hoje.</param>
public sealed record ReativarVencimentoCommand(Guid Id, Guid SetorId, string SetorSlug, DateOnly NovaData);

/// <summary>Reativa um vencimento encerrado com uma nova data, abrindo um ciclo novo de avisos.</summary>
public sealed class ReativarVencimentoHandler(
	IItemVencimentoRepository repository,
	RelogioDeVencimentos relogio,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de reativação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(ReativarVencimentoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var item = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (item is null || item.SetorId != command.SetorId)
		{
			return IntranetErrors.Vencimentos.NotFound;
		}

		if (!item.EstaEncerrado)
		{
			return IntranetErrors.Vencimentos.NaoEncerrado;
		}

		var dataAnterior = item.DataVencimento;

		try
		{
			item.Reativar(command.NovaData, relogio.Hoje());
		}
		catch (DomainInvariantException)
		{
			return IntranetErrors.Vencimentos.DataDeRenovacaoInvalida;
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.VencimentoReativar,
					RecursosDeAuditoria.Vencimento,
					item.Id.ToString(),
					ApoioDeVencimentos.Metadata(item, command.SetorSlug, dataAnterior, item.DataVencimento)),
				cancellationToken)
			.ConfigureAwait(false);

		return item.Id;
	}
}
