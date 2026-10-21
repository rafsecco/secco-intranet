using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Application.Tenants.Credenciais;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Models.Tenants;
using Secco.Intranet.Web.ViewComponents;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Web.Controllers;

/// <summary>
/// Credenciais OAuth de um tenant administrado (subsistema 4 da Área de tenants): emitir, alterar,
/// rotacionar e revogar. Mesma cadeia de filtros da Área de tenants; o SecureGate ligado não é exigido
/// aqui, porque a credencial de máquina existe sem ele. O tenant e o client id vêm só da rota e o
/// secret só aparece na resposta do POST que o criou: nunca em TempData, cookie, redirect, log ou trilha.
/// </summary>
/// <param name="cabecalho">Cabeçalho do tenant.</param>
/// <param name="leitura">Leitura das credenciais e das opções do formulário.</param>
/// <param name="escrita">Emissão, alteração, rotação e revogação.</param>
/// <param name="configuracao">Configuração do host (Authority do SecureGate).</param>
[Route("tenants/{tenantId:guid}/credenciais")]
[SomenteIntranetAdmin]
[ExigeSegundoFator]
[TenantAdministrado]
public sealed class TenantsCredenciaisController(
	ObterCabecalhoDoTenantHandler cabecalho,
	LeituraDeCredenciaisHandler leitura,
	CredenciaisDoTenantHandler escrita,
	IConfiguration configuracao) : Controller
{
	/// <summary>Credenciais do tenant.</summary>
	[HttpGet("")]
	public async Task<IActionResult> Index([FromRoute] Guid tenantId, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var lista = await leitura.ListarAsync(tenantId, cancellationToken);

		return lista.IsFailure ? Falha(lista.Error) : View(new TenantsCredenciaisIndexViewModel(topo.Value, lista.Value));
	}

	/// <summary>Formulário de emissão; tipo inválido ou ausente vale máquina.</summary>
	[HttpGet("nova")]
	public async Task<IActionResult> Nova([FromRoute] Guid tenantId, string? tipo, CancellationToken cancellationToken = default) =>
		await TelaNova(tenantId, LerTipo(tipo), new EnvioDaCredencial(), null, cancellationToken);

	/// <summary>
	/// Emite a credencial. Sucesso: a resposta <b>é</b> a tela do secret — nunca redirect com TempData, que
	/// gravaria o secret num cookie — e sai com <c>no-store</c>. Erro: volta ao formulário com os campos.
	/// </summary>
	[HttpPost("nova")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Nova([FromRoute] Guid tenantId, EnvioDaCredencial envio, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(envio);

		var tipo = LerTipo(envio.Tipo);
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var emitida = await escrita.EmitirAsync(tenantId, tipo, envio.ParaFormulario(), cancellationToken);

		if (emitida.IsFailure)
		{
			return emitida.Error.Type is ErrorType.Validation or ErrorType.Conflict
				? await TelaNova(tenantId, tipo, envio, emitida.Error.Description, cancellationToken)
				: Falha(emitida.Error);
		}

		return TelaDoSecret(topo.Value, emitida.Value.Secret, rotacao: false);
	}

	/// <summary>Detalhe de uma credencial.</summary>
	[HttpGet("{clientId}")]
	public async Task<IActionResult> Detalhe([FromRoute] Guid tenantId, [FromRoute] string clientId, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var credencial = await leitura.ObterAsync(tenantId, clientId, cancellationToken);

		if (credencial.IsFailure)
		{
			return Falha(credencial.Error);
		}

		var opcoes = await leitura.OpcoesAsync(tenantId, cancellationToken);

		if (opcoes.IsFailure)
		{
			return Falha(opcoes.Error);
		}

		var atual = credencial.Value;
		var campos = new EnvioDaCredencial
		{
			Tipo = atual.Tipo == TipoDeCredencial.Login ? "login" : "maquina",
			Nome = atual.Nome,
			Escopos = [.. atual.Escopos],
			Perfis = [.. atual.Perfis],
			RedirectUris = string.Join('\n', atual.RedirectUris),
			RedirectUrisDeLogout = string.Join('\n', atual.RedirectUrisDeLogout),
		};

		return View(new TenantsCredenciaisDetalheViewModel(topo.Value, atual, opcoes.Value, campos));
	}

	/// <summary>Altera a credencial. Sem secret na volta: sucesso ou erro, sempre redirect para o detalhe.</summary>
	[HttpPost("{clientId}")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Alterar(
		[FromRoute] Guid tenantId, [FromRoute] string clientId, EnvioDaCredencial envio, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(envio);

		return Concluir(
			await escrita.AlterarAsync(tenantId, clientId, envio.ParaFormulario(), cancellationToken),
			"Credencial alterada.",
			Url.Action(nameof(Detalhe), new { tenantId, clientId })!);
	}

	/// <summary>Troca o secret. Como na emissão, a resposta é a tela do secret, com <c>no-store</c>.</summary>
	[HttpPost("{clientId}/rotacionar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Rotacionar([FromRoute] Guid tenantId, [FromRoute] string clientId, CancellationToken cancellationToken = default)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var rotacionado = await escrita.RotacionarAsync(tenantId, clientId, cancellationToken);

		return rotacionado.IsSuccess
			? TelaDoSecret(topo.Value, rotacionado.Value, rotacao: true)
			: Concluir(Result.Failure(rotacionado.Error), string.Empty, Url.Action(nameof(Detalhe), new { tenantId, clientId })!);
	}

	/// <summary>Revoga a credencial, se a confirmação for o nome dela.</summary>
	[HttpPost("{clientId}/revogar")]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Revogar(
		[FromRoute] Guid tenantId, [FromRoute] string clientId, string? confirmacao, CancellationToken cancellationToken = default)
	{
		var revogada = await escrita.RevogarAsync(tenantId, clientId, confirmacao, cancellationToken);

		return Concluir(
			revogada,
			"Credencial revogada. Tokens já emitidos valem até 5 minutos.",
			revogada.IsSuccess ? Url.Action(nameof(Index), new { tenantId })! : Url.Action(nameof(Detalhe), new { tenantId, clientId })!);
	}

	private static TipoDeCredencial LerTipo(string? tipo) =>
		string.Equals(tipo, "login", StringComparison.OrdinalIgnoreCase) ? TipoDeCredencial.Login : TipoDeCredencial.Maquina;

	private async Task<IActionResult> TelaNova(
		Guid tenantId, TipoDeCredencial tipo, EnvioDaCredencial campos, string? erro, CancellationToken cancellationToken)
	{
		var topo = await cabecalho.HandleAsync(tenantId, cancellationToken);

		if (topo.IsFailure)
		{
			return Falha(topo.Error);
		}

		var opcoes = await leitura.OpcoesAsync(tenantId, cancellationToken);

		return opcoes.IsFailure
			? Falha(opcoes.Error)
			: View("Nova", new TenantsCredenciaisNovaViewModel(topo.Value, opcoes.Value, tipo, campos, erro));
	}

	/// <summary>A única saída que carrega o secret: tela, sem cache e sem TempData.</summary>
	private ViewResult TelaDoSecret(CabecalhoDoTenantDto topo, SecretDaCredencial secret, bool rotacao)
	{
		Response.Headers.CacheControl = "no-store";
		Response.Headers.Pragma = "no-cache";

		return View("Secret", new TenantsCredenciaisSecretViewModel(topo, secret, configuracao["Secco:SecureGate:Authority"] ?? string.Empty, rotacao));
	}

	/// <summary>NotFound vira 404; o resto vai para a mensagem na tela de destino, que nunca leva dado do secret.</summary>
	private IActionResult Concluir(Result resultado, string sucesso, string destino)
	{
		if (resultado.IsFailure && resultado.Error.Type == ErrorType.NotFound)
		{
			return NotFound();
		}

		TempData[resultado.IsSuccess ? FeedbackViewComponent.ChaveDaMensagem : FeedbackViewComponent.ChaveDaMensagemDeErro] =
			resultado.IsSuccess ? sucesso : resultado.Error.Description;

		return Redirect(destino);
	}

	private IActionResult Falha(Error erro) =>
		erro.Type == ErrorType.NotFound
			? NotFound()
			: View("~/Views/Tenants/Indisponivel.cshtml", new TenantsIndisponivelViewModel(erro.Description));
}
