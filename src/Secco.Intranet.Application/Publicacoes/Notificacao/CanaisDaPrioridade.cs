using Secco.Intranet.Domain.Publicacoes;

namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// Prioridade decide canal. Quem publica escolhe a urgência, não as caixinhas: escolher canal
/// é decisão de produto, e ninguém erra menos marcando opções uma a uma.
/// </summary>
internal static class CanaisDaPrioridade
{
	/// <summary>Item no inbox in-app.</summary>
	internal const string InApp = "in_app";

	/// <summary>Envio por e-mail.</summary>
	internal const string Email = "email";

	/// <summary>Canal do Microsoft Teams.</summary>
	internal const string Teams = "teams";

	/// <summary>Canal do Slack.</summary>
	internal const string Slack = "slack";

	/// <summary>Canais de uma urgência.</summary>
	/// <param name="prioridade">Urgência escolhida por quem publica.</param>
	internal static IReadOnlyList<string> De(PrioridadePublicacao prioridade) => prioridade switch
	{
		PrioridadePublicacao.Urgente => [InApp, Email, Teams, Slack],
		PrioridadePublicacao.Importante => [InApp, Email],
		_ => [InApp],
	};
}
