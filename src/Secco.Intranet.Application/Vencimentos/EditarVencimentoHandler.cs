using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.SharedKernel.Exceptions;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Pedido de edição de um vencimento.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="SetorId">Setor da rota; item de outro setor não é encontrado.</param>
/// <param name="SetorSlug">Slug do setor.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre.</param>
/// <param name="DataVencimento">Data do vencimento; mudá-la abre um ciclo novo de avisos.</param>
/// <param name="ResponsavelId">Usuário responsável.</param>
/// <param name="Antecedencias">Marcos em dias.</param>
public sealed record EditarVencimentoCommand(
	Guid Id,
	Guid SetorId,
	string SetorSlug,
	string? Titulo,
	string? Descricao,
	DateOnly DataVencimento,
	Guid? ResponsavelId,
	IReadOnlyList<int> Antecedencias);

/// <summary>Altera os dados de um vencimento ativo.</summary>
public sealed class EditarVencimentoHandler(
	IItemVencimentoRepository repository,
	IDiretorioDeUsuarios diretorio,
	IntranetOptions limites,
	ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Pedido de edição.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(EditarVencimentoCommand command, CancellationToken cancellationToken = default)
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

		var item = await repository.GetParaEdicaoAsync(command.Id, cancellationToken).ConfigureAwait(false);

		if (item is null || item.SetorId != command.SetorId)
		{
			return IntranetErrors.Vencimentos.NotFound;
		}

		if (item.EstaEncerrado)
		{
			return IntranetErrors.Vencimentos.Encerrado;
		}

		// Só confere o acesso quando o responsável muda: quem já era responsável e perdeu a
		// Role não pode travar a edição dos demais campos.
		if (command.ResponsavelId is { } responsavelId
			&& responsavelId != item.ResponsavelId
			&& !await ApoioDeVencimentos
				.ResponsavelTemAcessoAsync(diretorio, responsavelId, command.SetorSlug, cancellationToken)
				.ConfigureAwait(false))
		{
			return IntranetErrors.Vencimentos.ResponsavelSemAcesso;
		}

		var dataAnterior = item.DataVencimento;
		bool dataMudou;

		try
		{
			dataMudou = item.Editar(titulo, command.Descricao, command.DataVencimento, command.ResponsavelId, command.Antecedencias);
		}
		catch (DomainInvariantException)
		{
			return IntranetErrors.Vencimentos.AntecedenciasInvalidas;
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await trilha
			.RegistrarAsync(
				new RegistroDeAuditoria(
					VerbosDeAuditoria.VencimentoEditar,
					RecursosDeAuditoria.Vencimento,
					item.Id.ToString(),
					dataMudou
						? ApoioDeVencimentos.Metadata(item, command.SetorSlug, dataAnterior, item.DataVencimento)
						: ApoioDeVencimentos.Metadata(item, command.SetorSlug)),
				cancellationToken)
			.ConfigureAwait(false);

		return item.Id;
	}
}
