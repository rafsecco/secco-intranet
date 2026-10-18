using Secco.Intranet.Application.Acesso;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>Perfil de um tenant administrado — sem a classificação setor/produto, que é convenção da Intranet.</summary>
/// <param name="Nome">Nome.</param>
/// <param name="TotalDePermissoes">Quantidade de permissões.</param>
/// <param name="Reservado">Se é reservado da plataforma.</param>
public sealed record PerfilDoTenantDto(string Nome, int TotalDePermissoes, bool Reservado);

/// <summary>Detalhe de um perfil de tenant administrado.</summary>
/// <param name="Nome">Nome.</param>
/// <param name="Permissoes">Permissões gravadas.</param>
/// <param name="Reservado">Se é reservado da plataforma.</param>
/// <param name="TotalDeMembros">Quantidade de membros.</param>
/// <param name="PermissoesForaDoCatalogo">
/// Gravadas no perfil, mas que nenhum produto declara no catálogo (ADR-0039 da plataforma): não
/// concedem nada e só saem por ação do admin.
/// </param>
public sealed record PerfilDoTenantDetalheDto(
	string Nome,
	IReadOnlyList<string> Permissoes,
	bool Reservado,
	int TotalDeMembros,
	IReadOnlyList<string> PermissoesForaDoCatalogo);

/// <summary>Uma permissão declarada por um produto no catálogo do SecureGate.</summary>
/// <param name="Nome">Nome canônico (<c>recurso:acao</c>).</param>
/// <param name="Descricao">O que ela concede, como o produto descreveu.</param>
public sealed record PermissaoDoCatalogoDto(string Nome, string Descricao);

/// <summary>As permissões que um produto publicou no catálogo do SecureGate.</summary>
/// <param name="Produto">Produto (ex.: <c>compras</c>).</param>
/// <param name="Permissoes">Permissões do produto.</param>
public sealed record ProdutoDoCatalogoDto(string Produto, IReadOnlyList<PermissaoDoCatalogoDto> Permissoes);

/// <summary>Detalhe de uma pessoa do tenant administrado.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Situacao">Situação da conta.</param>
/// <param name="BloqueadoAte">Fim do bloqueio, quando bloqueado.</param>
/// <param name="Perfis">Perfis atribuídos.</param>
/// <param name="LoginsExternos">Provedores de login externo vinculados.</param>
/// <param name="DoisFatoresAtivo">Se o segundo fator está ligado.</param>
/// <param name="TemSenha">Se já definiu senha.</param>
/// <param name="EntraPorSenha">Se a conta entra por senha (falso: só pelo Entra).</param>
public sealed record UsuarioDoTenantDetalheDto(
	Guid Id,
	string Email,
	string? Nome,
	SituacaoDoUsuario Situacao,
	DateTimeOffset? BloqueadoAte,
	IReadOnlyList<string> Perfis,
	IReadOnlyList<string> LoginsExternos,
	bool DoisFatoresAtivo,
	bool TemSenha,
	bool EntraPorSenha);

/// <summary>Dados de uma pessoa a cadastrar.</summary>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição (opcional).</param>
/// <param name="Perfis">Perfis atribuídos na criação.</param>
/// <param name="EntraPorSenha"><c>true</c>: convite para definir senha; <c>false</c>: só pelo Entra.</param>
public sealed record NovoUsuarioDoTenant(string Email, string? Nome, IReadOnlyList<string> Perfis, bool EntraPorSenha);
