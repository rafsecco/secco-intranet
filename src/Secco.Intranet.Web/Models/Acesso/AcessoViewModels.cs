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
}

/// <summary>Modelo de <c>/Acesso</c>: uma das duas abas preenchida.</summary>
/// <param name="Aba">Aba ativa.</param>
/// <param name="Perfis">Conteúdo da aba Perfis (nulo na aba Usuários).</param>
/// <param name="Usuarios">Conteúdo da aba Usuários (nulo na aba Perfis).</param>
/// <param name="Busca">Filtro de e-mail aplicado, para repopular a busca.</param>
public sealed record AcessoIndexViewModel(
	AbaDoAcesso Aba, PerfisDaTelaDto? Perfis, PagedResult<UsuarioDto>? Usuarios, string? Busca);

/// <summary>Modelo da página que explica por que a gestão de acesso não abriu.</summary>
/// <param name="Mensagem">Texto do erro, já sem detalhe interno.</param>
public sealed record IndisponivelViewModel(string Mensagem);
