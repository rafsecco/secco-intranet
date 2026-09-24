using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Acesso;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Gestão de acesso: perfis (Roles) e usuários do tenant, no SecureGate. Exclusivo do
/// <c>intranet-admin</c> (ADR-0008) — o atributo na classe cobre toda action, presente e futura.
/// Controller fino (ADR-0002): só orquestra handlers.
/// </summary>
/// <param name="listarPerfis">Lista de perfis.</param>
/// <param name="listarUsuarios">Lista de usuários.</param>
/// <param name="criarPerfil">Criação de perfil.</param>
[SomenteIntranetAdmin]
public sealed class AcessoController(
	ListarPerfisHandler listarPerfis,
	ListarUsuariosHandler listarUsuarios,
	CriarPerfilHandler criarPerfil) : Controller
{
	/// <summary>Tela inicial, com as abas Perfis e Usuários.</summary>
	/// <param name="aba"><c>usuarios</c> abre a aba de usuários; qualquer outro valor, a de perfis.</param>
	/// <param name="busca">Trecho de e-mail (aba Usuários).</param>
	/// <param name="page">Página da aba Usuários.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Index(string? aba, string? busca, int page = 1, CancellationToken cancellationToken = default)
	{
		if (string.Equals(aba, "usuarios", StringComparison.OrdinalIgnoreCase))
		{
			var usuarios = await listarUsuarios.HandleAsync(new ListarUsuariosQuery(busca, page), cancellationToken);

			return usuarios.IsFailure
				? Falha(usuarios.Error)
				: View(new AcessoIndexViewModel(AbaDoAcesso.Usuarios, null, usuarios.Value, busca));
		}

		var perfis = await listarPerfis.HandleAsync(cancellationToken);

		return perfis.IsFailure
			? Falha(perfis.Error)
			: View(new AcessoIndexViewModel(AbaDoAcesso.Perfis, perfis.Value, null, null));
	}

	/// <summary>Cria um perfil (livre, ou um dos perfis do produto que ainda faltam).</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CriarPerfil(string? nome, CancellationToken cancellationToken = default)
	{
		var resultado = await criarPerfil.HandleAsync(nome ?? string.Empty, cancellationToken);

		return Concluir(resultado, $"Perfil \"{nome?.Trim()}\" criado.", nameof(Index));
	}

	/// <summary>
	/// Erro de leitura: perfil/usuário inexistente é 404; o resto (SecureGate ausente ou fora do
	/// ar) vira a página que explica, com 200 — não é falha da Intranet.
	/// </summary>
	private IActionResult Falha(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("Indisponivel", new IndisponivelViewModel(erro.Description));

	/// <summary>Grava o resultado da ação no toast certo e volta para a tela de destino.</summary>
	private RedirectToActionResult Concluir(Result resultado, string sucesso, string action, object? rota = null)
	{
		if (resultado.IsSuccess)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = sucesso;
		}
		else
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = resultado.Error.Description;
		}

		return RedirectToAction(action, rota);
	}
}
