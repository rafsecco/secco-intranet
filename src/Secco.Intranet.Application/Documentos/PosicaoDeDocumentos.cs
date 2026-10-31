using Secco.Intranet.Domain.Documentos;

namespace Secco.Intranet.Application.Documentos;

/// <summary>Cálculo da posição de um item novo entre os irmãos ativos, renumerando-os de zero a n-1.</summary>
internal static class PosicaoDeDocumentos
{
	/// <summary>
	/// Abre o lugar do item novo e devolve a <c>Ordem</c> que ele deve ter. Os irmãos ficam
	/// rastreados e alterados em memória; quem grava (a inclusão do item novo) leva tudo na mesma
	/// gravação.
	/// </summary>
	/// <param name="repository">Persistência de documentos.</param>
	/// <param name="setorId">Setor dono.</param>
	/// <param name="parentId">Pai do item novo; <c>null</c> na raiz.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Zero na raiz (o novo entra no topo e os demais descem); o fim dos filhos abaixo de um pai.</returns>
	public static async Task<int> AbrirLugarAsync(
		IDocumentoRepository repository,
		Guid setorId,
		Guid? parentId,
		CancellationToken cancellationToken)
	{
		var irmaos = await repository.ListarIrmaosAsync(setorId, parentId, cancellationToken).ConfigureAwait(false);
		var noTopo = parentId is null;

		for (var indice = 0; indice < irmaos.Count; indice++)
		{
			irmaos[indice].Posicionar(parentId, noTopo ? indice + 1 : indice);
		}

		return noTopo ? 0 : irmaos.Count;
	}

	/// <summary>Indica se o documento pode ser pai: ativo, da raiz e do setor informado.</summary>
	/// <param name="pai">Candidato a pai, com o setor dono.</param>
	/// <param name="setorSlug">Slug do setor onde o item novo será criado.</param>
	public static bool PodeSerPai(DocumentoComSetor? pai, string setorSlug) =>
		pai is not null
		&& pai.Documento.Ativo
		&& pai.Documento.ParentId is null
		&& string.Equals(pai.SetorSlug, setorSlug, StringComparison.OrdinalIgnoreCase);
}
