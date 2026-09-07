using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>Comando de publicação.</summary>
/// <param name="SetorSlug">Slug do setor dono.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Corpo">Corpo em Markdown.</param>
/// <param name="Tipo">Natureza.</param>
/// <param name="Visibilidade">Quem enxerga.</param>
/// <param name="Prioridade">Urgência.</param>
/// <param name="PublicadoEm">Entrada no ar.</param>
/// <param name="ExpiraEm">Expiração.</param>
/// <param name="CriadoPor">Quem publica.</param>
public sealed record PublicarPublicacaoCommand(
	string? SetorSlug,
	string? Titulo,
	string? Corpo,
	TipoPublicacao Tipo,
	Visibilidade Visibilidade,
	PrioridadePublicacao Prioridade,
	DateTimeOffset PublicadoEm,
	DateTimeOffset? ExpiraEm,
	string CriadoPor);

/// <summary>Cria uma publicação no setor informado.</summary>
/// <param name="repository">Persistência de publicações.</param>
/// <param name="setorRepository">Consulta de setores.</param>
/// <param name="limites">Limites de entrada do produto.</param>
public sealed class PublicarPublicacaoHandler(
	IPublicacaoRepository repository,
	ISetorRepository setorRepository,
	IntranetOptions limites)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PublicacaoDto>> HandleAsync(
		PublicarPublicacaoCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var erro = ValidacaoDePublicacao.Validar(
			command.Titulo, command.Corpo, command.PublicadoEm, command.ExpiraEm, limites.MaxNameLength);

		if (erro is not null)
		{
			return erro;
		}

		if (string.IsNullOrWhiteSpace(command.SetorSlug))
		{
			return IntranetErrors.Setores.NotFound;
		}

		var setor = await setorRepository.GetBySlugAsync(command.SetorSlug, cancellationToken).ConfigureAwait(false);

		if (setor is null || !setor.Ativo)
		{
			return IntranetErrors.Setores.NotFound;
		}

		var publicacao = new Publicacao(
			setor.Id, command.Titulo!, command.Corpo!, command.Tipo, command.Visibilidade,
			command.Prioridade, command.PublicadoEm, command.ExpiraEm, command.CriadoPor);

		await repository.AddAsync(publicacao, cancellationToken).ConfigureAwait(false);

		return Projecao.De(new PublicacaoComSetor(publicacao, setor.Nome, setor.Slug));
	}
}
