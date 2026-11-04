namespace Secco.Intranet.Web.Extensibility;

/// <summary>Um filtro aplicado à listagem exportada, como a tela o mostra.</summary>
/// <param name="Nome">Rótulo do filtro, por exemplo <c>Período</c>.</param>
/// <param name="Valor">Valor aplicado. A trilha guarda no máximo 200 caracteres dele.</param>
public sealed record FiltroDeExportacao(string Nome, string Valor);

/// <summary>O que a trilha registra de uma exportação. Nunca o conteúdo do documento.</summary>
/// <param name="Recurso">
/// Chave fixa do que foi exportado, definida no código da RCL e nunca vinda do usuário: de 1 a 64
/// caracteres, só <c>[a-z0-9-]</c>. É o que identifica o relatório na trilha.
/// </param>
/// <param name="Formato"><c>xlsx</c> ou <c>pdf</c>.</param>
/// <param name="Linhas">Quantas linhas saíram (<c>ExportedDocument.RowCount</c>).</param>
/// <param name="Filtros">Os mesmos filtros passados ao <c>ExportRequest.Filters</c>; vazio quando não há.</param>
public sealed record ExportacaoParaAuditar(
	string Recurso,
	string Formato,
	int Linhas,
	IReadOnlyList<FiltroDeExportacao> Filtros);

/// <summary>
/// Trilha de auditoria da exportação, síncrona e fail-closed (ADR-0058 da plataforma, ADR-0013):
/// a RCL gera o documento, registra aqui e só então envia. Resolvida do contêiner do host, a RCL só
/// injeta. Ao contrário da trilha das demais ações, ela <b>recusa</b> quando não consegue registrar.
/// </summary>
public interface ITrilhaDeExportacao
{
	/// <summary>Registra a exportação antes do envio.</summary>
	/// <param name="exportacao">O que foi exportado.</param>
	/// <param name="cancellationToken">Cancelamento da requisição; cancelado pelo chamador, propaga.</param>
	/// <returns>
	/// <c>true</c> quando a exportação foi registrada e o documento pode ser enviado; <c>false</c>
	/// quando não foi, e então a RCL descarta o documento e não o envia.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// Recurso fora da regra, formato desconhecido, linhas negativas ou filtros nulos: erro de
	/// programador, não falha de infraestrutura.
	/// </exception>
	Task<bool> RegistrarAsync(ExportacaoParaAuditar exportacao, CancellationToken cancellationToken);
}
