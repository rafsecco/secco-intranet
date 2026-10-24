namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>Árvore de quem reporta a quem. O tema decide como ela parece; o core decide quem está nela.</summary>
/// <param name="Raizes">Quem tem equipe e não tem gestor ativo, em ordem de nome.</param>
public sealed record OrganogramaModel(IReadOnlyList<NoOrganogramaModel> Raizes);
