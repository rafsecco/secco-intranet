using Secco.Intranet.Domain;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Pedido dos dados atuais de um documento para o formulário de edição.</summary>
/// <param name="DocumentoId">Identificador do documento.</param>
/// <param name="Dono">Dono da rota; precisa ser o dono do documento.</param>
/// <param name="DonosAdministrados">Setores e bibliotecas que o usuário administra.</param>
/// <param name="ExigirVinculo">
/// Quando <c>false</c>, dispensa a checagem de vínculo — modo aberto de DEV. Em produção é sempre <c>true</c>.
/// </param>
public sealed record ObterDocumentoParaEdicaoQuery(
	Guid DocumentoId,
	ReferenciaDeDono Dono,
	IReadOnlySet<ReferenciaDeDono> DonosAdministrados,
	bool ExigirVinculo);

/// <summary>Dados atuais de um documento, para preencher o formulário de edição.</summary>
/// <param name="Id">Identificador do documento.</param>
/// <param name="EhGrupo">Se o item é um grupo, sem arquivo nem visibilidade própria.</param>
/// <param name="Titulo">Título atual.</param>
/// <param name="Descricao">Descrição atual.</param>
/// <param name="Visibilidade">Visibilidade atual.</param>
/// <param name="NomeArquivo">Nome do arquivo atual; nulo no grupo.</param>
public sealed record DocumentoParaEdicaoDto(
	Guid Id,
	bool EhGrupo,
	string Titulo,
	string? Descricao,
	Visibilidade Visibilidade,
	string? NomeArquivo);

/// <summary>Devolve os dados de um documento ao alcance de quem administra o dono.</summary>
/// <param name="repository">Persistência de documentos.</param>
public sealed class ObterDocumentoParaEdicaoHandler(IDocumentoRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="query">Pedido.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<DocumentoParaEdicaoDto>> HandleAsync(
		ObterDocumentoParaEdicaoQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		var encontrado = await repository.GetByIdAsync(query.DocumentoId, cancellationToken).ConfigureAwait(false);

		if (!Alcanca(encontrado, query.Dono, query.DonosAdministrados, query.ExigirVinculo))
		{
			return Result.Failure<DocumentoParaEdicaoDto>(IntranetErrors.Documentos.NotFound);
		}

		var documento = encontrado!.Documento;

		return new DocumentoParaEdicaoDto(
			documento.Id,
			documento.EhGrupo,
			documento.Titulo,
			documento.Descricao,
			documento.Visibilidade,
			documento.NomeArquivo);
	}

	/// <summary>
	/// Regra de alcance compartilhada pela leitura e pela edição: o documento existe, está ativo,
	/// pertence ao dono da rota e, quando exigido, o usuário administra esse dono. Os casos
	/// negativos compartilham o mesmo erro para não revelar a existência do documento.
	/// </summary>
	/// <param name="encontrado">Documento com o dono, ou <c>null</c>.</param>
	/// <param name="dono">Dono da rota.</param>
	/// <param name="administrados">Setores e bibliotecas que o usuário administra.</param>
	/// <param name="exigirVinculo">Se o vínculo com o dono é exigido.</param>
	internal static bool Alcanca(
		DocumentoComDono? encontrado,
		ReferenciaDeDono dono,
		IReadOnlySet<ReferenciaDeDono> administrados,
		bool exigirVinculo) =>
		encontrado is { Documento.Ativo: true }
		&& encontrado.Dono == dono
		&& (!exigirVinculo || administrados.Contains(encontrado.Dono));
}
