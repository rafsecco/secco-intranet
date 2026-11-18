using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Cadastra uma sala de reunião.</summary>
public sealed class CriarSalaHandler(ISalaRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de cadastro; o <c>Id</c> é ignorado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(SalvarSalaCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var validado = ApoioDeSalas.Validar(command);

		if (validado.IsFailure)
		{
			return validado.Error;
		}

		var dados = validado.Value;

		if (await repository.NomeEmUsoAsync(dados.Nome, null, cancellationToken).ConfigureAwait(false))
		{
			return IntranetErrors.Salas.NomeEmUso;
		}

		var sala = new Sala(dados.Nome, dados.Localizacao, dados.Capacidade, dados.Recursos, dados.EmailNaAgenda, dados.Ordem);

		if (!command.Ativa)
		{
			sala.Desativar();
		}

		await repository.AddAsync(sala, cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.SalaCriar, RecursosDeAuditoria.Sala, sala.Id.ToString(), ApoioDeSalas.Metadata(sala)),
				cancellationToken)
			.ConfigureAwait(false);

		return sala.Id;
	}
}
