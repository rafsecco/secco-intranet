using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Pedido de edição.</summary>
/// <param name="PublicacaoId">Identificador da publicação.</param>
/// <param name="SetoresAdministrados">Setores que o usuário administra.</param>
/// <param name="ExigirVinculo">Quando <c>false</c>, dispensa a checagem.</param>
/// <param name="Titulo">Novo título.</param>
/// <param name="Corpo">Novo corpo em Markdown.</param>
/// <param name="Tipo">Nova natureza.</param>
/// <param name="Visibilidade">Nova visibilidade.</param>
/// <param name="Prioridade">Nova urgência.</param>
/// <param name="PublicadoEm">Nova entrada no ar.</param>
/// <param name="ExpiraEm">Nova expiração.</param>
public sealed record EditarPublicacaoCommand(
	Guid PublicacaoId,
	IReadOnlySet<string> SetoresAdministrados,
	bool ExigirVinculo,
	string? Titulo,
	string? Corpo,
	TipoPublicacao Tipo,
	Visibilidade Visibilidade,
	PrioridadePublicacao Prioridade,
	DateTimeOffset PublicadoEm,
	DateTimeOffset? ExpiraEm);

/// <summary>Altera conteúdo e agendamento de uma publicação existente.</summary>
/// <param name="repository">Persistência de publicações.</param>
/// <param name="limites">Limites de entrada do produto.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarPublicacaoHandler(
	IPublicacaoRepository repository,
	IntranetOptions limites,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PublicacaoDto>> HandleAsync(
		EditarPublicacaoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var erro = ValidacaoDePublicacao.Validar(
			command.Titulo, command.Corpo, command.PublicadoEm, command.ExpiraEm, limites.MaxNameLength);

		if (erro is not null)
		{
			return erro;
		}

		var encontrada = await repository.GetByIdAsync(command.PublicacaoId, cancellationToken).ConfigureAwait(false);

		if (encontrada is null
			|| (command.ExigirVinculo && !command.SetoresAdministrados.Contains(encontrada.SetorSlug)))
		{
			return IntranetErrors.Publicacoes.NotFound;
		}

		encontrada.Publicacao.Editar(
			command.Titulo!, command.Corpo!, command.Tipo, command.Visibilidade,
			command.Prioridade, command.PublicadoEm, command.ExpiraEm);

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.MuralEditar,
					RecursosDeAuditoria.Publicacao,
					encontrada.Publicacao.Id.ToString(),
					JsonSerializer.Serialize(new
					{
						titulo = encontrada.Publicacao.Titulo,
						tipo = encontrada.Publicacao.Tipo.ToString(),
						visibilidade = encontrada.Publicacao.Visibilidade.ToString(),
						prioridade = encontrada.Publicacao.Prioridade.ToString(),
						setor = encontrada.SetorSlug,
					})),
				cancellationToken)
			.ConfigureAwait(false);

		return Projecao.De(encontrada);
	}
}
