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
	/// <param name="dono">Dono dos itens.</param>
	/// <param name="parentId">Pai do item novo; <c>null</c> na raiz.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Zero na raiz (o novo entra no topo e os demais descem); o fim dos filhos abaixo de um pai.</returns>
	public static async Task<int> AbrirLugarAsync(
		IDocumentoRepository repository,
		DonoDeDocumento dono,
		Guid? parentId,
		CancellationToken cancellationToken)
	{
		var irmaos = await repository.ListarIrmaosAsync(dono, parentId, cancellationToken).ConfigureAwait(false);
		var noTopo = parentId is null;

		for (var indice = 0; indice < irmaos.Count; indice++)
		{
			irmaos[indice].Posicionar(parentId, noTopo ? indice + 1 : indice);
		}

		return noTopo ? 0 : irmaos.Count;
	}

	/// <summary>Renumera os irmãos de zero a n-1 sob o pai informado, na ordem da lista.</summary>
	/// <param name="irmaos">Irmãos na ordem desejada, já rastreados.</param>
	/// <param name="parentId">Pai comum; <c>null</c> na raiz.</param>
	public static void Renumerar(IReadOnlyList<Documento> irmaos, Guid? parentId)
	{
		for (var indice = 0; indice < irmaos.Count; indice++)
		{
			irmaos[indice].Posicionar(parentId, indice);
		}
	}

	/// <summary>Indica se o documento pode ser pai: ativo, da raiz e do dono informado.</summary>
	/// <param name="pai">Candidato a pai, com o dono.</param>
	/// <param name="dono">Dono onde o item novo será criado.</param>
	public static bool PodeSerPai(DocumentoComDono? pai, ReferenciaDeDono dono) =>
		pai is not null
		&& pai.Documento.Ativo
		&& pai.Documento.ParentId is null
		&& pai.Dono == dono;
}
