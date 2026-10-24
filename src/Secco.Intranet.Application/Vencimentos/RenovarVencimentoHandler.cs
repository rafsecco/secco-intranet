using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Vencimentos;
using Secco.SharedKernel.Exceptions;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Pedido de renovação de um vencimento.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="SetorId">Setor da rota; item de outro setor não é encontrado.</param>
/// <param name="SetorSlug">Slug do setor.</param>
/// <param name="NovaData">Nova data de vencimento; deve ser posterior à atual e a hoje.</param>
/// <param name="Observacao">Observação livre registrada no histórico.</param>
public sealed record RenovarVencimentoCommand(Guid Id, Guid SetorId, string SetorSlug, DateOnly NovaData, string? Observacao);

/// <summary>Renova um vencimento para uma data posterior e guarda o histórico.</summary>
public sealed class RenovarVencimentoHandler(
	IItemVencimentoRepository repository,
	RelogioDeVencimentos relogio,
	IAtorAtual ator,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de renovação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(RenovarVencimentoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var item = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (item is null || item.SetorId != command.SetorId)
		{
			return IntranetErrors.Vencimentos.NotFound;
		}

		if (item.EstaEncerrado)
		{
			return IntranetErrors.Vencimentos.Encerrado;
		}

		var dataAnterior = item.DataVencimento;
		var renovadoPor = ator.Atual()?.Nome ?? ApoioDeVencimentos.AutorDoSistema;

		RenovacaoDeVencimento renovacao;

		try
		{
			renovacao = item.Renovar(command.NovaData, relogio.Hoje(), renovadoPor, command.Observacao);
		}
		catch (DomainInvariantException)
		{
			return IntranetErrors.Vencimentos.DataDeRenovacaoInvalida;
		}

		await repository.AdicionarRenovacaoAsync(renovacao, cancellationToken).ConfigureAwait(false);
		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.VencimentoRenovar,
					RecursosDeAuditoria.Vencimento,
					item.Id.ToString(),
					ApoioDeVencimentos.Metadata(item, command.SetorSlug, dataAnterior, item.DataVencimento)),
				cancellationToken)
			.ConfigureAwait(false);

		return item.Id;
	}
}
