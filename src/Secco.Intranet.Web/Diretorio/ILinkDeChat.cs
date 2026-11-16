using System.Security.Claims;

namespace Secco.Intranet.Web.Diretorio;

/// <summary>Monta o endereço que abre uma conversa com uma pessoa do Diretório. Só monta: nunca chama o provedor.</summary>
public interface ILinkDeChat
{
	/// <summary>
	/// Endereço da conversa com a pessoa, ou <c>null</c>: provedor desligado, sem e-mail válido, ou a
	/// pessoa é quem está vendo.
	/// </summary>
	/// <param name="email">E-mail da pessoa.</param>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="quemVe">Usuário logado.</param>
	string? Para(string? email, Guid usuarioId, ClaimsPrincipal quemVe);

	/// <summary>Nome do provedor para o rótulo ("Teams"), ou <c>null</c> com o chat desligado.</summary>
	string? NomeDoProvedor { get; }
}
