using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Tenants;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Perfis, pessoas e federação de um tenant administrado (subsistema 2 da Área de tenants). Mesma
/// cadeia de filtros da Área de tenants, mais o SecureGate ligado no cadastro. O tenant vem só da
/// rota: nenhum parâmetro de tenant é lido de form ou query.
/// </summary>
[Route("tenants/{tenantId:guid}/acesso")]
[SomenteIntranetAdmin]
[ExigeSegundoFator]
[TenantAdministrado]
[SecureGateLigado]
public sealed class TenantsAcessoController(
	ObterCabecalhoDoTenantHandler cabecalho,
	LeituraDoAcessoDoTenantHandler leitura,
	PerfisDoTenantHandler perfis,
	UsuariosDoTenantHandler usuarios,
	FederacaoDoTenantHandler federacao) : Controller
{
	/// <summary>Abas Perfis, Usuários e Federação.</summary>
	[HttpGet("")]
	public async Task<IActionResult> Index(
		[FromRoute] Guid tenantId, string? aba, string? busca, int page = 1, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		if (string.Equals(aba, "usuarios", StringComparison.OrdinalIgnoreCase))
		{
			var lista = await leitura.ListarUsuariosAsync(tenantId, busca, page, cancellationToken);

			return lista.IsFailure
				? Falha(lista.Error)
				: View(new TenantsAcessoIndexViewModel(topo.Value, AbaDoAcessoDoTenant.Usuarios, null, lista.Value, busca, null));
		}

		if (string.Equals(aba, "federacao", StringComparison.OrdinalIgnoreCase))
		{
			var lida = await leitura.ObterFederacaoAsync(tenantId, cancellationToken);

			return lida.IsFailure
				? Falha(lida.Error)
				: View(new TenantsAcessoIndexViewModel(topo.Value, AbaDoAcessoDoTenant.Federacao, null, null, null, lida.Value));
		}

		var lidos = await leitura.ListarPerfisAsync(tenantId, cancellationToken);

		return lidos.IsFailure
			? Falha(lidos.Error)
			: View(new TenantsAcessoIndexViewModel(topo.Value, AbaDoAcessoDoTenant.Perfis, lidos.Value, null, null, null));
	}

	/// <summary>Cria um perfil.</summary>
	[HttpPost("perfis")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CriarPerfil([FromRoute] Guid tenantId, string? nome, CancellationToken cancellationToken = default) =>
		Concluir(await perfis.CriarAsync(tenantId, nome, cancellationToken), "Perfil criado.", Url.Action(nameof(Index), new { tenantId })!);

	/// <summary>Detalhe de um perfil.</summary>
	[HttpGet("perfis/{nome}")]
	public async Task<IActionResult> Perfil([FromRoute] Guid tenantId, [FromRoute] string nome, int page = 1, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var tela = await leitura.ObterPerfilAsync(tenantId, nome, page, cancellationToken);

		return tela.IsFailure ? Falha(tela.Error) : View(new TenantsAcessoPerfilViewModel(topo.Value, tela.Value));
	}

	/// <summary>Exclui um perfil sem membros.</summary>
	[HttpPost("perfis/{nome}/excluir")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ExcluirPerfil([FromRoute] Guid tenantId, [FromRoute] string nome, CancellationToken cancellationToken = default)
	{
		var excluido = await perfis.ExcluirAsync(tenantId, nome, cancellationToken);

		return Concluir(
			excluido,
			"Perfil excluído.",
			excluido.IsSuccess ? Url.Action(nameof(Index), new { tenantId })! : Url.Action(nameof(Perfil), new { tenantId, nome })!);
	}

	/// <summary>Substitui as permissões do perfil.</summary>
	[HttpPost("perfis/{nome}/permissoes")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EditarPermissoes(
		[FromRoute] Guid tenantId, [FromRoute] string nome, string[]? permissoes, CancellationToken cancellationToken = default) =>
		Concluir(await perfis.DefinirPermissoesAsync(tenantId, nome, permissoes, cancellationToken),
			"Permissões salvas.", Url.Action(nameof(Perfil), new { tenantId, nome })!);

	/// <summary>Adiciona um membro a partir da tela do perfil.</summary>
	[HttpPost("perfis/{nome}/membros")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AdicionarMembro(
		[FromRoute] Guid tenantId, [FromRoute] string nome, Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.AtribuirPerfilAsync(tenantId, usuarioId, nome, cancellationToken),
			"Membro adicionado.", Url.Action(nameof(Perfil), new { tenantId, nome })!);

	/// <summary>Formulário de cadastro.</summary>
	[HttpGet("usuarios/novo")]
	public async Task<IActionResult> Novo([FromRoute] Guid tenantId, CancellationToken cancellationToken = default)
	{
		var modelo = new TenantsAcessoNovoViewModel();

		return await PreencherNovo(tenantId, modelo, cancellationToken) ?? View(modelo);
	}

	/// <summary>Cadastra a pessoa.</summary>
	[HttpPost("usuarios/novo")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Novo([FromRoute] Guid tenantId, TenantsAcessoNovoViewModel modelo, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(modelo);

		var criado = await usuarios.CadastrarAsync(
			tenantId, new CadastrarUsuarioDoTenantCommand(modelo.Email, modelo.Nome, modelo.Perfis, modelo.EntraPorSenha), cancellationToken);

		if (criado.IsSuccess)
		{
			TempData[FeedbackViewComponent.ChaveDaMensagem] = criado.Value.IncluidaComoMembro
				? "Esta pessoa já tinha conta: foi incluída como membro e entra com a senha que já usa."
				: modelo.EntraPorSenha
					? "Pessoa cadastrada. O SecureGate enviou o convite para ela definir a senha."
					: "Pessoa cadastrada. Ela entra pelo Entra ID da empresa.";

			return Redirect(Url.Action(nameof(Usuario), new { tenantId, usuarioId = criado.Value.Id })!);
		}

		modelo.Erro = criado.Error.Description;

		return await PreencherNovo(tenantId, modelo, cancellationToken) ?? View(modelo);
	}

	/// <summary>Detalhe de uma pessoa.</summary>
	[HttpGet("usuarios/{usuarioId:guid}")]
	public async Task<IActionResult> Usuario([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var tela = await leitura.ObterUsuarioAsync(tenantId, usuarioId, cancellationToken);

		return tela.IsFailure ? Falha(tela.Error) : View(new TenantsAcessoUsuarioViewModel(topo.Value, tela.Value));
	}

	/// <summary>Atribui um perfil.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/perfis")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> AtribuirPerfil(
		[FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, string? perfil, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.AtribuirPerfilAsync(tenantId, usuarioId, perfil, cancellationToken),
			"Perfil atribuído.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Retira um perfil; <paramref name="origem"/> = <c>perfil</c> volta à tela do perfil.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/perfis/retirar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RetirarPerfil(
		[FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, string? perfil, string? origem, CancellationToken cancellationToken = default) =>
		Concluir(
			await usuarios.RetirarPerfilAsync(tenantId, usuarioId, perfil, cancellationToken),
			"Perfil retirado.",
			string.Equals(origem, "perfil", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(perfil)
				? Url.Action(nameof(Perfil), new { tenantId, nome = perfil })!
				: Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Desativa a conta.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/desativar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Desativar([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.DesativarAsync(tenantId, usuarioId, cancellationToken), "Conta desativada.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Reativa a conta.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/reativar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Reativar([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.ReativarAsync(tenantId, usuarioId, cancellationToken), "Conta reativada.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Encerra as sessões.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/sessoes/encerrar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EncerrarSessoes([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.EncerrarSessoesAsync(tenantId, usuarioId, cancellationToken),
			"Sessões encerradas. Vale nos sistemas em até alguns minutos.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Reenvia o convite.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/convite")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ReenviarConvite([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.ReenviarConviteAsync(tenantId, usuarioId, cancellationToken), "Convite reenviado.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Suspende o vínculo de um membro.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/vinculo/suspender")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SuspenderVinculo([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.SuspenderVinculoAsync(tenantId, usuarioId, cancellationToken),
			"Vínculo suspenso. A pessoa não entra mais neste sistema até ser reativada.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Reativa o vínculo de um membro.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/vinculo/reativar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> ReativarVinculo([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default) =>
		Concluir(await usuarios.ReativarVinculoAsync(tenantId, usuarioId, cancellationToken),
			"Vínculo reativado.", Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);

	/// <summary>Remove o vínculo de um membro e os perfis dele neste tenant.</summary>
	[HttpPost("usuarios/{usuarioId:guid}/vinculo/remover")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> RemoverVinculo([FromRoute] Guid tenantId, [FromRoute] Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var removido = await usuarios.RemoverVinculoAsync(tenantId, usuarioId, cancellationToken);

		return Concluir(
			removido,
			"Vínculo removido. A pessoa saiu deste sistema e os perfis dela aqui foram apagados.",
			removido.IsSuccess
				? Url.Action(nameof(Index), new { tenantId, aba = "usuarios" })!
				: Url.Action(nameof(Usuario), new { tenantId, usuarioId })!);
	}

	/// <summary>Define a federação.</summary>
	[HttpPost("federacao")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SalvarFederacao(
		[FromRoute] Guid tenantId, string? directoryId, bool habilitada, bool confirmarTroca, CancellationToken cancellationToken = default) =>
		Concluir(
			await federacao.DefinirAsync(tenantId, new DefinirFederacaoDoTenantCommand(directoryId, habilitada, confirmarTroca), cancellationToken),
			habilitada ? "Federação com o Entra ID habilitada." : "Federação com o Entra ID desabilitada.",
			Url.Action(nameof(Index), new { tenantId, aba = "federacao" })!);

	private async Task<IActionResult?> PreencherNovo(Guid tenantId, TenantsAcessoNovoViewModel modelo, CancellationToken cancellationToken)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var atribuiveis = await leitura.PerfisAtribuiveisAsync(tenantId, cancellationToken);

		if (atribuiveis.IsFailure)
		{
			return Falha(atribuiveis.Error);
		}

		var lida = await leitura.ObterFederacaoAsync(tenantId, cancellationToken);

		if (lida.IsFailure)
		{
			return Falha(lida.Error);
		}

		modelo.Tenant = topo.Value;
		modelo.PerfisDisponiveis = atribuiveis.Value;
		modelo.FederacaoHabilitada = lida.Value.Habilitada;

		return null;
	}

	private RedirectResult Concluir(Result resultado, string sucesso, string destino)
	{
		TempData[resultado.IsSuccess ? FeedbackViewComponent.ChaveDaMensagem : FeedbackViewComponent.ChaveDaMensagemDeErro] =
			resultado.IsSuccess ? sucesso : resultado.Error.Description;

		return Redirect(destino);
	}

	private IActionResult Falha(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("~/Views/Tenants/Indisponivel.cshtml", new TenantsIndisponivelViewModel(erro.Description));
}
