using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.Theming.Contracts;

namespace Secco.Intranet.Web.ViewComponents;

/// <summary>
/// Identidade do usuário para o menu do avatar. Como o <see cref="NavigationViewComponent"/>,
/// resolve os dados no core e deixa o markup para o tema.
/// </summary>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
/// <param name="environment">Ambiente de hospedagem, para o modo aberto de DEV.</param>
public sealed class UserMenuViewComponent(IConfiguration configuration, IWebHostEnvironment environment) : ViewComponent
{
	/// <summary>Renderiza o menu do usuário.</summary>
	public IViewComponentResult Invoke()
	{
		var usuario = HttpContext.User;
		var autenticado = usuario.Identity?.IsAuthenticated == true;
		var nome = autenticado ? usuario.Identity?.Name ?? "Usuário" : "Visitante";

		// "Meu perfil" só existe para quem tem acesso ao Diretório; sem acesso, não há para onde apontar.
		var temAcesso = AcessoAdministrativo.ModoAbertoDeDev(environment, configuration)
			|| AcessoAoDiretorio.Nivel(usuario) != NivelDeAcessoAoDiretorio.Nenhum;
		var urlPerfil = temAcesso ? "/diretorio/perfil" : null;

		return View(new UserMenuModel(
			autenticado,
			nome,
			Iniciais(nome),
			urlPerfil,
			IntranetAuthenticationExtensions.IsConfigured(configuration)));
	}

	private static string Iniciais(string nome)
	{
		var partes = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		return partes.Length switch
		{
			0 => "?",
			1 => partes[0][..1].ToUpperInvariant(),
			_ => string.Concat(partes[0][..1], partes[^1][..1]).ToUpperInvariant(),
		};
	}
}
