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
/// <param name="Familia">
/// <c>true</c> quando é uma família (<c>prefixo-{param}:acao</c>, ADR-0043 da plataforma): não se
/// atribui pela tela, porque as instâncias são do próprio sistema.
/// </param>
public sealed record PermissaoDoCatalogoDto(string Nome, string Descricao, bool Familia = false);

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
/// <param name="Vinculo">Relação da pessoa com este tenant (ADR-0040 da plataforma).</param>
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
	bool EntraPorSenha,
	VinculoNoTenant Vinculo);

/// <summary>
/// Relação de uma pessoa com um tenant (ADR-0040 da plataforma): uma conta por pessoa, membro de
/// vários tenants. A conta é governada pelo tenant <b>dono</b>; nos demais, só o vínculo.
/// </summary>
public enum VinculoNoTenant
{
	/// <summary>A conta é deste tenant: convite, senha, ativar e desativar são daqui.</summary>
	Dono,

	/// <summary>Membro com conta em outro tenant: aqui, só o vínculo e os perfis.</summary>
	Membro,

	/// <summary>Membro com o vínculo suspenso: não entra neste sistema até ser reativado.</summary>
	Suspenso,
}

/// <summary>Linha da lista de pessoas de um tenant administrado.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Situacao">Situação da conta.</param>
/// <param name="Perfis">Perfis neste tenant.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Vinculo">Relação com este tenant.</param>
public sealed record UsuarioDoTenantDto(
	Guid Id, string Email, SituacaoDoUsuario Situacao, IReadOnlyList<string> Perfis, string? Nome, VinculoNoTenant Vinculo);

/// <summary>Resultado de adicionar uma pessoa a um tenant administrado.</summary>
/// <param name="Id">Id da pessoa no SecureGate.</param>
/// <param name="IncluidaComoMembro">
/// <c>true</c> quando o e-mail já tinha conta na instalação e a pessoa entrou como membro, mantendo
/// a credencial que tem; <c>false</c> quando a conta foi criada aqui.
/// </param>
public sealed record CadastroNoTenantDto(Guid Id, bool IncluidaComoMembro);

/// <summary>Dados de uma pessoa a cadastrar.</summary>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição (opcional).</param>
/// <param name="Perfis">Perfis atribuídos na criação.</param>
/// <param name="EntraPorSenha"><c>true</c>: convite para definir senha; <c>false</c>: só pelo Entra.</param>
public sealed record NovoUsuarioDoTenant(string Email, string? Nome, IReadOnlyList<string> Perfis, bool EntraPorSenha);
