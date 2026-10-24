namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Uma pessoa no organograma e a equipe direta dela.</summary>
/// <param name="Nome">Nome da pessoa.</param>
/// <param name="Cargo">Cargo; nulo quando não informado.</param>
/// <param name="Url">Endereço do perfil da pessoa.</param>
/// <param name="Avatar">Foto ou iniciais.</param>
/// <param name="GestorInativo">Se o gestor dela foi desativado: a pessoa continua no organograma, com a marca.</param>
/// <param name="Equipe">Quem reporta diretamente a ela; vazio em quem não tem equipe.</param>
public sealed record NoOrganogramaModel(
	string Nome,
	string? Cargo,
	string Url,
	AvatarModel Avatar,
	bool GestorInativo,
	IReadOnlyList<NoOrganogramaModel> Equipe);
