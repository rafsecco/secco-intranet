namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Identidade do usuário atual, para o menu do avatar na topbar.</summary>
/// <param name="Autenticado">Se há usuário autenticado.</param>
/// <param name="Nome">Nome de exibição; vazio quando anônimo.</param>
/// <param name="Iniciais">Uma ou duas iniciais para o avatar.</param>
/// <param name="UrlPerfil">Destino do link de perfil; <c>null</c> quando nao ha pagina de perfil.</param>
/// <param name="AutenticacaoHabilitada">
/// Se a autenticação está configurada. Em DEV sem SecureGate a Intranet roda em modo aberto,
/// e o tema não deve oferecer entrar nem sair.
/// </param>
public sealed record UserMenuModel(
	bool Autenticado,
	string Nome,
	string Iniciais,
	string? UrlPerfil,
	bool AutenticacaoHabilitada);
