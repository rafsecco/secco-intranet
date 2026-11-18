using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Altera os dados e a situação (ativa ou não) de uma sala.</summary>
public sealed class EditarSalaHandler(ISalaRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de edição; <c>Id</c> obrigatório.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(SalvarSalaCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var sala = command.Id is { } id
			? await repository.GetAsync(id, cancellationToken).ConfigureAwait(false)
			: null;

		if (sala is null)
		{
			return IntranetErrors.Salas.NotFound;
		}

		var validado = ApoioDeSalas.Validar(command);

		if (validado.IsFailure)
		{
			return validado.Error;
		}

		var dados = validado.Value;

		if (await repository.NomeEmUsoAsync(dados.Nome, sala.Id, cancellationToken).ConfigureAwait(false))
		{
			return IntranetErrors.Salas.NomeEmUso;
		}

		sala.Editar(dados.Nome, dados.Localizacao, dados.Capacidade, dados.Recursos, dados.EmailNaAgenda, dados.Ordem);

		if (command.Ativa)
		{
			sala.Ativar();
		}
		else
		{
			sala.Desativar();
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.SalaEditar, RecursosDeAuditoria.Sala, sala.Id.ToString(), ApoioDeSalas.Metadata(sala)),
				cancellationToken)
			.ConfigureAwait(false);

		return sala.Id;
	}
}
