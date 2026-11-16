using System.Security.Claims;
using Secco.Intranet.Web.Navigation;

namespace Secco.Intranet.Web.Diretorio;

/// <summary>Link de chat do Teams: <c>https://teams.microsoft.com/l/chat/0/0?users=e-mail</c>.</summary>
/// <param name="opcoes">Configuração já validada na subida.</param>
public sealed class LinkDeChat(ChatOptions opcoes) : ILinkDeChat
{
	private const string BaseDoTeams = "https://teams.microsoft.com/l/chat/0/0?users=";

	/// <inheritdoc />
	public string? NomeDoProvedor => opcoes.Provedor == ProvedorDeChat.Teams ? "Teams" : null;

	/// <inheritdoc />
	public string? Para(string? email, Guid usuarioId, ClaimsPrincipal quemVe)
	{
		if (opcoes.Provedor != ProvedorDeChat.Teams)
		{
			return null;
		}

		var endereco = email?.Trim();

		if (string.IsNullOrEmpty(endereco) || !endereco.Contains('@'))
		{
			return null;
		}

		// Sem claim sub, UsuarioId devolve null: ninguém é "a própria pessoa" e o endereço sai.
		if (AcessoAoDiretorio.UsuarioId(quemVe) == usuarioId)
		{
			return null;
		}

		var link = BaseDoTeams + Uri.EscapeDataString(endereco);

		return opcoes.TenantId is { } tenant ? $"{link}&tenantId={tenant:D}" : link;
	}
}
