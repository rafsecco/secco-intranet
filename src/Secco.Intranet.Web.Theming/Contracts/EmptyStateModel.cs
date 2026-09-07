namespace Secco.Intranet.Web.Theming.Contracts;

/// <summary>
/// Tela vazia. Um vazio é convite para agir, não recado de erro: por isso a ação é parte do
/// modelo, e o texto diz o que fazer em vez de lamentar a ausência.
/// </summary>
/// <param name="Icone">Classe do ícone.</param>
/// <param name="Titulo">O que ainda não existe aqui.</param>
/// <param name="Descricao">Como mudar isso (opcional).</param>
/// <param name="Acao">Ação que resolve o vazio (opcional).</param>
public sealed record EmptyStateModel(
	string Icone,
	string Titulo,
	string? Descricao = null,
	PageActionModel? Acao = null);
