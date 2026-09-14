using Secco.Intranet.Application.Setores;
using Secco.Intranet.Web.Theming.Contracts;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Entrada para a montagem do menu.</summary>
/// <param name="Setores">Setores que o usuário enxerga (ver <see cref="SetorAcesso.Visiveis"/>).</param>
/// <param name="CaminhoAtual">Caminho da requisição, usado para marcar o item ativo.</param>
/// <param name="MostrarAdministracao">Se o grupo de administração deve aparecer.</param>
/// <param name="DemoHabilitado">Se as páginas de demonstração estão ligadas.</param>
/// <param name="MostrarInventario">Se o item de menu do Inventário deve aparecer.</param>
public sealed record NavigationRequest(
	IReadOnlyList<SetorDto> Setores,
	string CaminhoAtual,
	bool MostrarAdministracao,
	bool DemoHabilitado,
	bool MostrarInventario);

/// <summary>
/// Fonte estática do menu. O Mural é o único item fixo — é o que fala com a empresa toda;
/// o resto da navegação é o conjunto de setores do usuário. A tabela autorecursiva
/// <c>ItemMenu</c> substitui esta fonte na Fase 2 sem tocar no tema.
/// </summary>
public static class IntranetNavigation
{
	/// <summary>Monta o menu para a requisição atual.</summary>
	/// <param name="request">Dados já resolvidos da requisição.</param>
	public static NavigationModel Build(NavigationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		var caminho = string.IsNullOrWhiteSpace(request.CaminhoAtual) ? "/" : request.CaminhoAtual;

		var principais = new List<NavigationItemModel>
		{
			new("Mural", "bi-megaphone", "/", EhMural(caminho)),
		};

		if (request.DemoHabilitado)
		{
			principais.Add(new NavigationItemModel(
				"Diretório", "bi-people", "/diretorio", Corresponde(caminho, "/diretorio")));
		}

		if (request.MostrarInventario)
		{
			principais.Add(new NavigationItemModel(
				"Inventário", "bi-box-seam", "/inventario", Corresponde(caminho, "/inventario")));
		}

		var setores = request.Setores
			.Select(setor => new NavigationItemModel(
				setor.Nome,
				setor.Icone,
				$"/setor/{setor.Slug}",
				Corresponde(caminho, $"/setor/{setor.Slug}"),
				setor.Slug))
			.ToList();

		var grupos = new List<NavigationGroupModel>
		{
			new(null, principais),
			new("Setores", setores),
		};

		if (request.MostrarAdministracao)
		{
			grupos.Add(new NavigationGroupModel("Administração",
			[
				new NavigationItemModel("Setores", "bi-sliders", "/setores", Corresponde(caminho, "/setores")),
			]));
		}

		return new NavigationModel(grupos);
	}

	private static bool EhMural(string caminho) =>
		caminho == "/" || Corresponde(caminho, "/mural");

	private static bool Corresponde(string caminho, string prefixo) =>
		caminho.Equals(prefixo, StringComparison.OrdinalIgnoreCase)
		|| caminho.StartsWith(prefixo + "/", StringComparison.OrdinalIgnoreCase);
}
