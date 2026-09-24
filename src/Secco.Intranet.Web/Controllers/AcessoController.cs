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
/// <param name="obterPerfil">Detalhe de perfil.</param>
/// <param name="criarPerfil">Criação de perfil.</param>
/// <param name="excluirPerfil">Exclusão de perfil.</param>
/// <param name="atribuirPerfil">Atribuição de perfil a usuário.</param>
/// <param name="retirarPerfil">Retirada de perfil de usuário.</param>
/// <param name="obterUsuario">Detalhe de usuário.</param>
/// <param name="desativarUsuario">Desativação de conta.</param>
/// <param name="reativarUsuario">Reativação de conta.</param>
/// <param name="encerrarSessoes">Encerramento de sessões.</param>
[SomenteIntranetAdmin]
public sealed class AcessoController(
	ListarPerfisHandler listarPerfis,
	ListarUsuariosHandler listarUsuarios,
	ObterPerfilHandler obterPerfil,
	ObterUsuarioHandler obterUsuario,
	CriarPerfilHandler criarPerfil,
	ExcluirPerfilHandler excluirPerfil,
	AtribuirPerfilHandler atribuirPerfil,
	RetirarPerfilHandler retirarPerfil,
	DesativarUsuarioHandler desativarUsuario,
	ReativarUsuarioHandler reativarUsuario,
	EncerrarSessoesHandler encerrarSessoes) : Controller
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

	/// <summary>Detalhe de um perfil: permissões (só leitura), membros e adicionar membro.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="page">Página de membros.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Perfil(string? nome, int page = 1, CancellationToken cancellationToken = default)
	{
		var tela = await obterPerfil.HandleAsync(new ObterPerfilQuery(nome ?? string.Empty, page), cancellationToken);

		return tela.IsFailure ? Falha(tela.Error) : View(new PerfilViewModel(tela.Value));
	}

	/// <summary>Exclui um perfil comum sem membros.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ExcluirPerfil(string? nome, CancellationToken cancellationToken = default)
	{
		var resultado = await excluirPerfil.HandleAsync(nome ?? string.Empty, cancellationToken);

		return resultado.IsSuccess
			? Concluir(resultado, $"Perfil \"{nome?.Trim()}\" excluído.", nameof(Index))
			: Concluir(resultado, string.Empty, nameof(Perfil), new { nome });
	}

	/// <summary>Atribui um perfil a um usuário; volta para a tela de origem.</summary>
	/// <param name="usuarioId">Usuário que recebe o perfil.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="origem"><c>perfil</c> ou <c>usuario</c> — só escolhe para onde voltar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AtribuirPerfil(Guid usuarioId, string? perfil, string? origem, CancellationToken cancellationToken = default)
	{
		var resultado = await atribuirPerfil.HandleAsync(new AtribuirPerfilCommand(usuarioId, perfil ?? string.Empty), cancellationToken);

		return ConcluirComOrigem(resultado, $"Perfil \"{perfil?.Trim()}\" atribuído.", origem, usuarioId, perfil);
	}

	/// <summary>Retira um perfil de um usuário; volta para a tela de origem.</summary>
	/// <param name="usuarioId">Usuário que perde o perfil.</param>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="origem"><c>perfil</c> ou <c>usuario</c> — só escolhe para onde voltar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RetirarPerfil(Guid usuarioId, string? perfil, string? origem, CancellationToken cancellationToken = default)
	{
		var resultado = await retirarPerfil.HandleAsync(new RetirarPerfilCommand(usuarioId, perfil ?? string.Empty), cancellationToken);

		return ConcluirComOrigem(resultado, $"Perfil \"{perfil?.Trim()}\" retirado.", origem, usuarioId, perfil);
	}

	/// <summary>Detalhe de um usuário: perfis por setor, adicionar/retirar, situação e sessões.</summary>
	/// <param name="id">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpGet]
	public async Task<IActionResult> Usuario(Guid id, CancellationToken cancellationToken = default)
	{
		var tela = await obterUsuario.HandleAsync(id, cancellationToken);

		return tela.IsFailure ? Falha(tela.Error) : View(new UsuarioViewModel(tela.Value));
	}

	/// <summary>
	/// Atribui a Role de um setor. Setor e papel chegam separados e o nome do perfil é composto
	/// aqui — só <c>admin</c> e <c>user</c> são papéis aceitos, então o formulário não consegue
	/// pedir um perfil arbitrário por esta rota.
	/// </summary>
	/// <param name="usuarioId">Usuário que recebe o perfil.</param>
	/// <param name="setor">Slug do setor.</param>
	/// <param name="papel"><c>admin</c> ou <c>user</c>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AtribuirPerfilDeSetor(Guid usuarioId, string? setor, string? papel, CancellationToken cancellationToken = default)
	{
		var sufixo = papel?.Trim().ToLowerInvariant() switch
		{
			"admin" => ClassificacaoDePerfil.SufixoAdmin,
			"user" => ClassificacaoDePerfil.SufixoUsuario,
			_ => null,
		};

		if (sufixo is null || string.IsNullOrWhiteSpace(setor))
		{
			TempData[FeedbackViewComponent.ChaveDaMensagemDeErro] = "Escolha o setor e o papel.";

			return RedirectToAction(nameof(Usuario), new { id = usuarioId });
		}

		var perfil = setor.Trim() + sufixo;
		var resultado = await atribuirPerfil.HandleAsync(new AtribuirPerfilCommand(usuarioId, perfil), cancellationToken);

		return Concluir(resultado, $"Perfil \"{perfil}\" atribuído.", nameof(Usuario), new { id = usuarioId });
	}

	/// <summary>Desativa a conta de um usuário.</summary>
	/// <param name="id">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> DesativarUsuario(Guid id, CancellationToken cancellationToken = default) =>
		Concluir(await desativarUsuario.HandleAsync(id, cancellationToken), "Conta desativada.", nameof(Usuario), new { id });

	/// <summary>Reativa a conta de um usuário.</summary>
	/// <param name="id">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ReativarUsuario(Guid id, CancellationToken cancellationToken = default) =>
		Concluir(await reativarUsuario.HandleAsync(id, cancellationToken), "Conta reativada.", nameof(Usuario), new { id });

	/// <summary>Encerra as sessões de um usuário.</summary>
	/// <param name="id">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EncerrarSessoes(Guid id, CancellationToken cancellationToken = default) =>
		Concluir(await encerrarSessoes.HandleAsync(id, cancellationToken), "Sessões encerradas.", nameof(Usuario), new { id });

	/// <summary>
	/// Volta para a tela de onde o formulário saiu. <paramref name="origem"/> é só uma escolha entre
	/// duas telas — o destino é sempre montado aqui, nunca lido do formulário (sem redirecionamento aberto).
	/// </summary>
	private RedirectToActionResult ConcluirComOrigem(Result resultado, string sucesso, string? origem, Guid usuarioId, string? perfil) =>
		string.Equals(origem, "usuario", StringComparison.OrdinalIgnoreCase)
			? Concluir(resultado, sucesso, "Usuario", new { id = usuarioId })
			: Concluir(resultado, sucesso, nameof(Perfil), new { nome = perfil?.Trim() });

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
