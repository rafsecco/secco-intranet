using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Models.Acesso;

/// <summary>Aba da tela inicial de acesso.</summary>
public enum AbaDoAcesso
{
	/// <summary>Perfis.</summary>
	Perfis = 0,

	/// <summary>Usuários.</summary>
	Usuarios = 1,

	/// <summary>Federação de login via Microsoft Entra ID.</summary>
	Federacao = 2,
}

/// <summary>Modelo de <c>/Acesso</c>: uma das três abas preenchida.</summary>
/// <param name="Aba">Aba ativa.</param>
/// <param name="Perfis">Conteúdo da aba Perfis (nulo fora dela).</param>
/// <param name="Usuarios">Conteúdo da aba Usuários (nulo fora dela).</param>
/// <param name="Busca">Filtro de e-mail aplicado, para repopular a busca.</param>
/// <param name="Federacao">Conteúdo da aba Federação (nulo fora dela).</param>
public sealed record AcessoIndexViewModel(
	AbaDoAcesso Aba, PerfisDaTelaDto? Perfis, PagedResult<UsuarioDto>? Usuarios, string? Busca, FederacaoDto? Federacao = null);

/// <summary>Modelo do detalhe de um perfil.</summary>
/// <param name="Tela">Perfil, membros da página e candidatos a membro.</param>
public sealed record PerfilViewModel(PerfilTelaDto Tela);

/// <summary>Modelo do detalhe de um usuário.</summary>
/// <param name="Tela">Usuário, perfis agrupados por setor e o que ainda pode ser atribuído.</param>
public sealed record UsuarioViewModel(UsuarioTelaDto Tela);

/// <summary>Modelo da página que explica por que a gestão de acesso não abriu.</summary>
/// <param name="Mensagem">Texto do erro, já sem detalhe interno.</param>
public sealed record IndisponivelViewModel(string Mensagem);
