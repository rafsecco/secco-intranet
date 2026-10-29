using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Theming.Contracts;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Entrada para a montagem do menu.</summary>
/// <param name="Setores">Setores que o usuário enxerga (filtrados por permissão em <see cref="Secco.Intranet.Web.ViewComponents.NavigationViewComponent"/>).</param>
/// <param name="CaminhoAtual">Caminho da requisição, usado para marcar o item ativo.</param>
/// <param name="MostrarAdministracao">Se o grupo de administração deve aparecer.</param>
/// <param name="MostrarDiretorio">Se o item Diretório deve aparecer (nível de acesso de Usuário ou acima).</param>
/// <param name="MostrarInventario">Se o item de menu do Inventário deve aparecer.</param>
/// <param name="Arvores">Itens ativos da árvore de cada setor, por SetorId (de <c>ListarArvoresDosSetoresHandler</c>); setor sem entrada não aparece.</param>
/// <param name="ItensExtras">Itens extras de projetos externos que o usuário pode ver.</param>
/// <param name="Posicoes">Posições salvas do menu do tenant; nulo = ordem padrão.</param>
public sealed record NavigationRequest(
	IReadOnlyList<SetorDto> Setores,
	string CaminhoAtual,
	bool MostrarAdministracao,
	bool MostrarDiretorio,
	bool MostrarInventario,
	IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>? Arvores = null,
	IReadOnlyList<ItemExtraDoMenu>? ItensExtras = null,
	PosicoesDoMenuDto? Posicoes = null);

/// <summary>
/// Montagem do menu. O Mural é o único item fixo que fala com a empresa toda; a seção Setores
/// vem da árvore <c>ItemMenu</c> de cada setor, e os demais itens fixos continuam aqui.
/// </summary>
public static class IntranetNavigation
{
	/// <summary>Monta o menu para a requisição atual.</summary>
	/// <param name="request">Dados já resolvidos da requisição.</param>
	public static NavigationModel Build(NavigationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		var caminho = string.IsNullOrWhiteSpace(request.CaminhoAtual) ? "/" : request.CaminhoAtual;

		var padrao = PrincipalNaOrdemPadrao(request.MostrarDiretorio, request.ItensExtras ?? []);

		var principais = OrdemDoMenu.Ordenar(padrao, entrada => entrada.Chave, request.Posicoes?.Principal)
			.Select(entrada => new NavigationItemModel(
				entrada.Rotulo,
				entrada.Icone,
				entrada.Url,
				entrada.Chave == CatalogoDeItensDoMenuPrincipal.ChaveMural
					? EhMural(caminho)
					: Corresponde(caminho, CaminhoDe(entrada.Url))))
			.ToList();

		var setores = OrdemDoMenu.Ordenar(request.Setores, setor => setor.Id.ToString("D"), request.Posicoes?.Setores)
			.Select(setor => SetorNoMenu(setor, request.Arvores, caminho))
			.OfType<NavigationItemModel>()
			.ToList();

		var grupos = new List<NavigationGroupModel>
		{
			new(null, principais),
			new("Setores", setores),
		};

		// O Inventário mora na Administração, mas tem gate próprio: quem só tem acesso a ele
		// (inventario-admin, inventario:read) vê o grupo só com esse item.
		var administracao = new List<NavigationItemModel>();

		if (request.MostrarAdministracao)
		{
			administracao.Add(new NavigationItemModel("Setores", "bi-sliders", "/setores", Corresponde(caminho, "/setores")));
			administracao.Add(new NavigationItemModel("Acesso", "bi-shield-lock", "/acesso", Corresponde(caminho, "/acesso")));
			administracao.Add(new NavigationItemModel("Menu", "bi-list-ol", "/menu", Corresponde(caminho, "/menu")));
		}

		if (request.MostrarInventario)
		{
			administracao.Add(new NavigationItemModel(
				"Inventário", "bi-box-seam", "/inventario", Corresponde(caminho, "/inventario")));
		}

		if (administracao.Count > 0)
		{
			grupos.Add(new NavigationGroupModel("Administração", administracao));
		}

		return new NavigationModel(grupos);
	}

	/// <summary>
	/// Entradas do grupo principal na ordem padrão: Mural, Diretório (se pedido) e os extras por
	/// <see cref="ItemExtraDoMenu.Ordem"/> e depois <see cref="ItemExtraDoMenu.Rotulo"/>.
	/// </summary>
	/// <param name="incluirDiretorio">Se o Diretório entra.</param>
	/// <param name="extras">Itens extras visíveis.</param>
	public static IReadOnlyList<EntradaDoMenuPrincipal> PrincipalNaOrdemPadrao(bool incluirDiretorio, IEnumerable<ItemExtraDoMenu> extras)
	{
		ArgumentNullException.ThrowIfNull(extras);

		var entradas = new List<EntradaDoMenuPrincipal>
		{
			new(CatalogoDeItensDoMenuPrincipal.ChaveMural, "Mural", "bi-megaphone", "/"),
		};

		if (incluirDiretorio)
		{
			entradas.Add(new EntradaDoMenuPrincipal(CatalogoDeItensDoMenuPrincipal.ChaveDiretorio, "Diretório", "bi-people", "/diretorio"));
		}

		entradas.AddRange(extras
			.OrderBy(extra => extra.Ordem)
			.ThenBy(extra => extra.Rotulo, StringComparer.OrdinalIgnoreCase)
			.Select(extra => new EntradaDoMenuPrincipal(extra.Chave, extra.Rotulo, extra.Icone, extra.Url)));

		return entradas;
	}

	/// <summary>Caminho de uma URL: sem query nem fragmento e sem a barra final (exceto a raiz).</summary>
	private static string CaminhoDe(string url)
	{
		var corte = url.IndexOfAny(['?', '#']);
		var caminho = corte < 0 ? url : url[..corte];
		var semBarra = caminho.TrimEnd('/');

		return semBarra.Length == 0 ? "/" : semBarra;
	}

	/// <summary>O setor como nó de nível 0, sem link; nulo se nada debaixo dele leva a algum lugar.</summary>
	private static NavigationItemModel? SetorNoMenu(
		SetorDto setor, IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>? arvores, string caminho)
	{
		// Setor criado antes da lista de reservados: a rota fixa vence a dele e o link daria 404.
		if (SlugsReservados.Contem(setor.Slug) || arvores is null || !arvores.TryGetValue(setor.Id, out var itens))
		{
			return null;
		}

		var raiz = itens.FirstOrDefault(item => item.Tipo == TipoDeItemMenu.Setor);

		if (raiz is null)
		{
			return null;
		}

		var filhos = Filhos(raiz.Id, $"/{setor.Slug}", itens, caminho);

		return filhos.Count == 0
			? null
			: new NavigationItemModel(setor.Nome, setor.Icone, Url: null, filhos.Exists(filho => filho.Ativo), setor.Slug, filhos);
	}

	/// <summary>
	/// Filhos de um nó, em ordem, já podados: nó sem destino e sem filhos visíveis some (de baixo
	/// para cima, então uma cadeia de agrupadores vazios some inteira).
	/// </summary>
	private static List<NavigationItemModel> Filhos(
		Guid paiId, string caminhoDoPai, IReadOnlyList<ItemMenuDto> itens, string caminhoAtual)
	{
		var resultado = new List<NavigationItemModel>();

		foreach (var item in itens.Where(item => item.ParentId == paiId).OrderBy(item => item.Ordem))
		{
			var caminhoDoItem = $"{caminhoDoPai}/{item.Slug}";
			var netos = Filhos(item.Id, caminhoDoItem, itens, caminhoAtual);
			var url = Destino(item, caminhoDoItem);

			if (url is null && netos.Count == 0)
			{
				continue;
			}

			var ativo = Corresponde(caminhoAtual, caminhoDoItem)
				|| (item.Rota is { } rota && rota.StartsWith('/') && Corresponde(caminhoAtual, rota))
				|| netos.Exists(neto => neto.Ativo);

			resultado.Add(new NavigationItemModel(item.Nome, item.Icone, url, ativo, Filhos: netos.Count == 0 ? null : netos));
		}

		return resultado;
	}

	/// <summary>Documentos/Avisos/Vencimentos abrem no caminho da árvore; Personalizado vai direto para a rota, se tiver.</summary>
	private static string? Destino(ItemMenuDto item, string caminhoDoItem) => item.Tipo switch
	{
		TipoDeItemMenu.Documentos or TipoDeItemMenu.Avisos or TipoDeItemMenu.Vencimentos => caminhoDoItem,
		TipoDeItemMenu.Personalizado when !string.IsNullOrWhiteSpace(item.Rota) => item.Rota,
		_ => null,
	};

	private static bool EhMural(string caminho) =>
		caminho == "/" || Corresponde(caminho, "/mural");

	private static bool Corresponde(string caminho, string prefixo) =>
		caminho.Equals(prefixo, StringComparison.OrdinalIgnoreCase)
		|| caminho.StartsWith(prefixo + "/", StringComparison.OrdinalIgnoreCase);
}
