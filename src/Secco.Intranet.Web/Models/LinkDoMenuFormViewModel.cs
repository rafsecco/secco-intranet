using Secco.Intranet.Application.Menu;

namespace Secco.Intranet.Web.Models;

/// <summary>Dados do formulário de um link do menu principal; o formato é conferido pelo caso de uso.</summary>
public sealed class LinkDoMenuFormViewModel
{
	/// <summary>Link em edição; <c>null</c> no formulário novo. Vem da rota, nunca do corpo.</summary>
	public Guid? Id { get; set; }

	/// <summary>Texto exibido no menu.</summary>
	public string? Rotulo { get; set; }

	/// <summary>Caminho da intranet ou endereço http/https.</summary>
	public string? Url { get; set; }

	/// <summary>Classe do Bootstrap Icons; vazio = sem ícone.</summary>
	public string? Icone { get; set; }

	/// <summary>
	/// Abre em nova aba. Sem valor inicial de propósito: caixa desmarcada não posta nada e o binding
	/// tem de dar <c>false</c>; quem marca o padrão do formulário novo é o GET.
	/// </summary>
	public bool NovaAba { get; set; }

	/// <summary>Formulário preenchido com um link existente.</summary>
	/// <param name="link">Link a editar.</param>
	public static LinkDoMenuFormViewModel De(LinkDoMenuPrincipalDto link)
	{
		ArgumentNullException.ThrowIfNull(link);

		return new LinkDoMenuFormViewModel { Id = link.Id, Rotulo = link.Rotulo, Url = link.Url, Icone = link.Icone, NovaAba = link.NovaAba };
	}
}
