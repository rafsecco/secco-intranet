using Microsoft.AspNetCore.Mvc;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Web.Theming.Contracts;
using Secco.SharedKernel.Constants;

namespace Secco.Intranet.Web.ViewComponents;

/// <summary>
/// Monta o sino e entrega ao markup do tema (ADR-0004). Está no layout de todas as páginas,
/// então nenhuma falha aqui pode derrubar a página: sem usuário identificado ou sem Hub
/// configurado, o sino simplesmente não aparece.
/// </summary>
/// <param name="caixa">Inbox in-app.</param>
public sealed class NotificacoesViewComponent(ICaixaDeNotificacoes caixa) : ViewComponent
{
	private const int LimiteNoSino = 10;

	/// <summary>Renderiza o sino.</summary>
	public async Task<IViewComponentResult> InvokeAsync()
	{
		if (!Guid.TryParse(HttpContext.User.FindFirst(SeccoClaims.Subject)?.Value, out var usuarioId))
		{
			return View(new NotificacoesModel(0, [], Habilitado: false));
		}

		var naoLidas = await caixa.NaoLidasAsync(usuarioId, HttpContext.RequestAborted).ConfigureAwait(false);

		var itens = naoLidas
			.OrderByDescending(notificacao => notificacao.CriadaEm)
			.Take(LimiteNoSino)
			.Select(notificacao => new NotificacaoNoSinoModel(
				notificacao.Id,
				notificacao.Titulo,
				notificacao.Mensagem,
				notificacao.Link,
				Quando(notificacao.CriadaEm)))
			.ToList();

		return View(new NotificacoesModel(naoLidas.Count, itens, Habilitado: true));
	}

	private static string Quando(DateTimeOffset criadaEm)
	{
		var decorrido = DateTimeOffset.UtcNow - criadaEm;

		return decorrido switch
		{
			{ TotalMinutes: < 1 } => "agora",
			{ TotalHours: < 1 } => $"há {(int)decorrido.TotalMinutes} min",
			{ TotalDays: < 1 } => $"há {(int)decorrido.TotalHours} h",
			_ => $"há {(int)decorrido.TotalDays} d",
		};
	}
}
