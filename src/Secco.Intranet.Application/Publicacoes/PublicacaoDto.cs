using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Application.Publicacoes;

/// <summary>
/// Representação de leitura. <paramref name="Corpo"/> é Markdown cru: a conversão para HTML
/// é apresentação e acontece na Web.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Corpo">Corpo em Markdown.</param>
/// <param name="Tipo">Natureza.</param>
/// <param name="Visibilidade">Quem enxerga.</param>
/// <param name="Prioridade">Urgência.</param>
/// <param name="PublicadoEm">Entrada no ar.</param>
/// <param name="ExpiraEm">Saída do ar.</param>
/// <param name="Ativo">Se não foi arquivada.</param>
/// <param name="SetorId">Identificador do setor dono.</param>
/// <param name="SetorNome">Nome do setor dono.</param>
/// <param name="SetorSlug">Slug do setor dono.</param>
/// <param name="CriadoPor">Quem publicou.</param>
public sealed record PublicacaoDto(
	Guid Id,
	string Titulo,
	string Corpo,
	TipoPublicacao Tipo,
	Visibilidade Visibilidade,
	PrioridadePublicacao Prioridade,
	DateTimeOffset PublicadoEm,
	DateTimeOffset? ExpiraEm,
	bool Ativo,
	Guid SetorId,
	string SetorNome,
	string SetorSlug,
	string CriadoPor)
{
	/// <summary>Se está no ar no instante informado — mesma regra do agregado.</summary>
	/// <param name="agora">Instante de referência.</param>
	public bool EstaNoAr(DateTimeOffset agora) =>
		Ativo && PublicadoEm <= agora && (ExpiraEm is null || ExpiraEm > agora);
}
