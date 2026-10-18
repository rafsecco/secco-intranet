namespace Secco.Intranet.Application.Documentos;

/// <summary>Endereço e chave de um arquivo recém-gravado.</summary>
/// <param name="CaminhoRelativo">Endereço opaco, montado pelo próprio armazenamento.</param>
/// <param name="ChaveEmbrulhada">Chave do arquivo cifrada pela chave mestra.</param>
/// <param name="Tamanho">Tamanho do conteúdo original, em bytes.</param>
public sealed record ArquivoGravado(string CaminhoRelativo, string ChaveEmbrulhada, long Tamanho);

/// <summary>Pastas lógicas aceitas pelo armazenamento para arquivos que não são documento.</summary>
public static class PastasDeArquivo
{
	/// <summary>Fotos dos colaboradores do Diretório.</summary>
	public const string FotosDoDiretorio = "diretorio/fotos";

	/// <summary>Indica se a pasta é uma das conhecidas.</summary>
	public static bool Contem(string pasta) => pasta is FotosDoDiretorio;
}

/// <summary>
/// Porta de armazenamento de arquivos. A cifragem acontece <b>dentro</b> da implementação:
/// quem chama nunca vê chave nem bytes em claro no repouso, e um backend em nuvem enxerga
/// apenas texto cifrado — trocar de backend não é uma decisão de confiança.
/// </summary>
/// <remarks>
/// O <c>CaminhoRelativo</c> é montado pela implementação a partir do tenant corrente, nunca
/// pelo chamador: é o que impede um bug de composição de caminho de cruzar a fronteira de
/// isolamento entre tenants (ADR-0005).
/// </remarks>
public interface IArquivoStore
{
	/// <summary>Grava um conteúdo, cifrando-o.</summary>
	/// <param name="conteudo">Fluxo do arquivo original.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<ArquivoGravado> GravarAsync(Stream conteudo, CancellationToken cancellationToken = default);

	/// <summary>Grava um conteúdo, cifrando-o, numa pasta lógica fora da dos documentos.</summary>
	/// <param name="conteudo">Fluxo do arquivo original.</param>
	/// <param name="pastaLogica">Uma das constantes de <see cref="PastasDeArquivo"/>; qualquer outro valor é recusado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <exception cref="ArgumentException">Se a pasta não estiver em <see cref="PastasDeArquivo"/>.</exception>
	Task<ArquivoGravado> GravarAsync(Stream conteudo, string pastaLogica, CancellationToken cancellationToken = default);

	/// <summary>
	/// Decifra um arquivo direto no destino informado — normalmente o corpo da resposta HTTP.
	/// Escreve em blocos: um arquivo grande nunca é materializado inteiro em memória.
	/// </summary>
	/// <param name="caminhoRelativo">Endereço devolvido por <see cref="GravarAsync(Stream, CancellationToken)"/>.</param>
	/// <param name="chaveEmbrulhada">Chave do arquivo cifrada pela chave mestra.</param>
	/// <param name="destino">Fluxo de destino.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task EscreverEmAsync(
		string caminhoRelativo,
		string chaveEmbrulhada,
		Stream destino,
		CancellationToken cancellationToken = default);

	/// <summary>Apaga um arquivo do armazenamento.</summary>
	/// <param name="caminhoRelativo">Endereço devolvido por <see cref="GravarAsync(Stream, CancellationToken)"/>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoverAsync(string caminhoRelativo, CancellationToken cancellationToken = default);
}
