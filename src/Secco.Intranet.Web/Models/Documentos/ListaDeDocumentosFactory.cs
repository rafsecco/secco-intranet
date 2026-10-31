using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Web.Theming.Contracts;

namespace Secco.Intranet.Web.Models.Documentos;

/// <summary>
/// Monta o <see cref="ListaDeDocumentosModel"/> da página de leitura. Toda regra de exibição
/// (URL, ícone, etiqueta, tamanho, data, badge, grupo vazio) mora aqui; o tema só desenha.
/// </summary>
public static class ListaDeDocumentosFactory
{
	/// <summary>Monta a lista a partir da árvore devolvida pelo caso de uso de listagem.</summary>
	/// <param name="nos">Itens da raiz com os filhos, em ordem.</param>
	/// <param name="urlDeDownload">Endereço do download de um documento, dado o identificador.</param>
	public static ListaDeDocumentosModel Montar(IReadOnlyList<NoDeDocumentoDto> nos, Func<Guid, string> urlDeDownload)
	{
		ArgumentNullException.ThrowIfNull(nos);
		ArgumentNullException.ThrowIfNull(urlDeDownload);

		var itens = new List<ItemDaListaDeDocumentosModel>();

		foreach (var no in nos)
		{
			// Um título sem nada embaixo parece erro na tela de leitura.
			if (no.Documento.Tipo == TipoDeDocumento.Grupo && no.Filhos.Count == 0)
			{
				continue;
			}

			itens.Add(Montar(no.Documento, urlDeDownload, [.. no.Filhos.Select(filho => Montar(filho, urlDeDownload, []))]));
		}

		return new ListaDeDocumentosModel(itens);
	}

	private static ItemDaListaDeDocumentosModel Montar(
		DocumentoDto documento,
		Func<Guid, string> urlDeDownload,
		IReadOnlyList<ItemDaListaDeDocumentosModel> filhos)
	{
		if (documento.Tipo == TipoDeDocumento.Grupo)
		{
			return new ItemDaListaDeDocumentosModel(
				documento.Titulo, documento.Descricao, true, null, null, null, null, null, filhos);
		}

		var badge = documento.Visibilidade == Visibilidade.Empresa
			? new BadgeModel("Toda a empresa", BadgeVariante.Neutro)
			: new BadgeModel(documento.SetorNome, BadgeVariante.Setor, documento.SetorSlug);

		return new ItemDaListaDeDocumentosModel(
			documento.Titulo,
			documento.Descricao,
			false,
			urlDeDownload(documento.Id),
			TiposDeArquivo.Icone(documento.NomeArquivo),
			TiposDeArquivo.Etiqueta(documento.NomeArquivo),
			$"{Tamanho(documento.Tamanho ?? 0)} · {documento.CreatedAt.ToLocalTime():dd/MM/yyyy}",
			badge,
			filhos);
	}

	private static string Tamanho(long bytes) => bytes switch
	{
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
		_ => $"{bytes / (1024d * 1024d):0.#} MB",
	};
}
