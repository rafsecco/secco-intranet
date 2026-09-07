using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;

namespace Secco.Intranet.Application.Documentos;

/// <summary>
/// Representação de leitura de um documento. Não carrega caminho nem chave — esses são
/// detalhes do armazenamento e não cruzam a borda da apresentação.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre.</param>
/// <param name="NomeArquivo">Nome original do arquivo.</param>
/// <param name="ContentType">Tipo apurado do conteúdo.</param>
/// <param name="Tamanho">Tamanho em bytes.</param>
/// <param name="Visibilidade">Quem enxerga o documento.</param>
/// <param name="SetorId">Identificador do setor dono.</param>
/// <param name="SetorNome">Nome do setor dono.</param>
/// <param name="SetorSlug">Slug do setor dono.</param>
/// <param name="CriadoPor">Quem publicou.</param>
/// <param name="CreatedAt">Momento da publicação.</param>
public sealed record DocumentoDto(
	Guid Id,
	string Titulo,
	string? Descricao,
	string NomeArquivo,
	string ContentType,
	long Tamanho,
	Visibilidade Visibilidade,
	Guid SetorId,
	string SetorNome,
	string SetorSlug,
	string CriadoPor,
	DateTimeOffset CreatedAt);
