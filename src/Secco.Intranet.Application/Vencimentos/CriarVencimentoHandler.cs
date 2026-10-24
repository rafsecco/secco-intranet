using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Domain.Vencimentos;
using Secco.SharedKernel.Exceptions;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Pedido de cadastro de um vencimento.</summary>
/// <param name="SetorId">Setor dono.</param>
/// <param name="SetorSlug">Slug do setor (confere a Role do responsável e entra na trilha).</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre.</param>
/// <param name="DataVencimento">Data do vencimento; pode estar no passado.</param>
/// <param name="ResponsavelId">Usuário responsável, destinatário dos avisos.</param>
/// <param name="Antecedencias">Marcos em dias; <c>null</c> usa 30, 7 e 1.</param>
public sealed record CriarVencimentoCommand(
	Guid SetorId,
	string SetorSlug,
	string? Titulo,
	string? Descricao,
	DateOnly DataVencimento,
	Guid? ResponsavelId,
	IReadOnlyList<int>? Antecedencias);

/// <summary>Cadastra um vencimento no setor.</summary>
public sealed class CriarVencimentoHandler(
	IItemVencimentoRepository repository,
	IDiretorioDeUsuarios diretorio,
	IntranetOptions limites,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de cadastro.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(CriarVencimentoCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrWhiteSpace(command.Titulo))
		{
			return IntranetErrors.Vencimentos.TituloRequired;
		}

		var titulo = command.Titulo.Trim();

		if (titulo.Length > limites.MaxNameLength)
		{
			return IntranetErrors.Vencimentos.TituloTooLong(limites.MaxNameLength);
		}

		if (command.ResponsavelId is { } responsavelId
			&& !await ApoioDeVencimentos
				.ResponsavelTemAcessoAsync(diretorio, responsavelId, command.SetorSlug, cancellationToken)
				.ConfigureAwait(false))
		{
			return IntranetErrors.Vencimentos.ResponsavelSemAcesso;
		}

		ItemVencimento item;

		try
		{
			item = new ItemVencimento(
				command.SetorId, titulo, command.Descricao, command.DataVencimento, command.ResponsavelId, command.Antecedencias);
		}
		catch (DomainInvariantException)
		{
			return IntranetErrors.Vencimentos.AntecedenciasInvalidas;
		}

		await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);
		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.VencimentoCriar,
					RecursosDeAuditoria.Vencimento,
					item.Id.ToString(),
					ApoioDeVencimentos.Metadata(item, command.SetorSlug)),
				cancellationToken)
			.ConfigureAwait(false);

		return item.Id;
	}
}
