namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Intenção visual de um aviso de feedback.</summary>
public enum ToastVariante
{
	/// <summary>Confirmação de uma ação que deu certo.</summary>
	Sucesso = 0,

	/// <summary>Falha que o usuário precisa saber, sem impedir o uso da página.</summary>
	Erro = 1,
}

/// <summary>
/// Aviso efêmero de resultado de ação — o que antes cada view escrevia como
/// <c>&lt;div class="alert"&gt;</c> à mão. O core decide o texto, o tema decide como o aviso
/// aparece (ADR-0004).
/// </summary>
/// <param name="Texto">Mensagem já pronta para exibição.</param>
/// <param name="Variante">
/// Intenção visual. <see cref="ToastVariante.Erro"/> ainda não tem produtor no core, e existe
/// desde já porque o contrato atravessa dois temas: acrescentar o valor depois obrigaria a
/// mexer nos dois de novo — mesma razão de o <see cref="BadgeVariante"/> ter nascido com cinco.
/// </param>
public sealed record ToastModel(string Texto, ToastVariante Variante = ToastVariante.Sucesso);
