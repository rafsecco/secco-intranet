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
/// <param name="NomeArquivo">Nome original do arquivo; nulo no grupo.</param>
/// <param name="ContentType">Tipo apurado do conteúdo; nulo no grupo.</param>
/// <param name="Tamanho">Tamanho em bytes; nulo no grupo.</param>
/// <param name="Visibilidade">Quem enxerga o documento.</param>
/// <param name="Dono">Referência ao dono: setor ou biblioteca.</param>
/// <param name="DonoNome">Nome do dono.</param>
/// <param name="CriadoPor">Quem publicou.</param>
/// <param name="CreatedAt">Momento da publicação.</param>
/// <param name="Tipo">Arquivo ou grupo.</param>
/// <param name="ParentId">Grupo que contém o item; <c>null</c> na raiz.</param>
/// <param name="Ordem">Posição entre os irmãos.</param>
public sealed record DocumentoDto(
	Guid Id,
	string Titulo,
	string? Descricao,
	string? NomeArquivo,
	string? ContentType,
	long? Tamanho,
	Visibilidade Visibilidade,
	ReferenciaDeDono Dono,
	string DonoNome,
	string CriadoPor,
	DateTimeOffset CreatedAt,
	TipoDeDocumento Tipo = TipoDeDocumento.Arquivo,
	Guid? ParentId = null,
	int Ordem = 0);
