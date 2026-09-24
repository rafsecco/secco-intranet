namespace Secco.Intranet.Application.Acesso;

/// <summary>De onde o perfil vem, pelo nome — a plataforma não guarda essa distinção.</summary>
public enum TipoDePerfil
{
	/// <summary>Criado à mão pelo <c>intranet-admin</c> (ex.: <c>gerente-de-compras</c>).</summary>
	Comum = 0,

	/// <summary>Role de setor: <c>{slug}-admin</c> ou <c>{slug}-user</c> (ADR-0001).</summary>
	Setor = 1,

	/// <summary>Perfil fixo do produto: <c>intranet-admin</c> ou <c>inventario-admin</c>.</summary>
	Produto = 2,
}

/// <summary>Situação da conta, como o SecureGate a informa.</summary>
public enum SituacaoDoUsuario
{
	/// <summary>Valor que a Intranet não reconhece.</summary>
	Desconhecida = 0,

	/// <summary>Conta ativa.</summary>
	Ativo = 1,

	/// <summary>Conta desativada.</summary>
	Desativado = 2,

	/// <summary>Conta bloqueada por tentativas de login.</summary>
	Bloqueado = 3,
}

/// <summary>Papel de uma Role de setor.</summary>
public enum PapelNoSetor
{
	/// <summary><c>{slug}-user</c>: só lê.</summary>
	Usuario = 0,

	/// <summary><c>{slug}-admin</c>: altera o próprio setor.</summary>
	Administrador = 1,
}

/// <summary>Perfil na listagem.</summary>
/// <param name="Nome">Nome do perfil (a Role no SecureGate).</param>
/// <param name="TotalDePermissoes">Quantas permissões o perfil carrega.</param>
/// <param name="Tipo">Classificação pelo nome.</param>
/// <param name="Reservado">Se é reservado da plataforma.</param>
public sealed record PerfilDto(string Nome, int TotalDePermissoes, TipoDePerfil Tipo, bool Reservado);

/// <summary>Detalhe de um perfil.</summary>
/// <param name="Nome">Nome do perfil.</param>
/// <param name="Permissoes">Permissões <c>recurso:acao</c>, somente leitura neste corte.</param>
/// <param name="Reservado">Se a plataforma o marca como reservado.</param>
/// <param name="TotalDeMembros">Quantos usuários o têm.</param>
/// <param name="Tipo">Classificação pelo nome.</param>
public sealed record PerfilDetalheDto(
	string Nome, IReadOnlyList<string> Permissoes, bool Reservado, int TotalDeMembros, TipoDePerfil Tipo);

/// <summary>Um membro de um perfil.</summary>
/// <param name="UsuarioId">Identificador do usuário.</param>
/// <param name="Email">E-mail — a identidade exibível, o SecureGate não guarda nome.</param>
/// <param name="Situacao">Situação da conta.</param>
public sealed record MembroDoPerfilDto(Guid UsuarioId, string Email, SituacaoDoUsuario Situacao);

/// <summary>Página de membros de um perfil.</summary>
/// <param name="Itens">Membros da página.</param>
/// <param name="Pagina">Página atual (1-based).</param>
/// <param name="TotalDePaginas">Total de páginas.</param>
/// <param name="Total">Total de membros.</param>
public sealed record PaginaDeMembros(
	IReadOnlyList<MembroDoPerfilDto> Itens, int Pagina, int TotalDePaginas, long Total);

/// <summary>Usuário na listagem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Situacao">Situação da conta.</param>
/// <param name="Perfis">Perfis atribuídos.</param>
public sealed record UsuarioDto(Guid Id, string Email, SituacaoDoUsuario Situacao, IReadOnlyList<string> Perfis);

/// <summary>Detalhe de um usuário.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Situacao">Situação da conta.</param>
/// <param name="BloqueadoAte">Fim do bloqueio, quando bloqueado.</param>
/// <param name="Perfis">Perfis atribuídos.</param>
/// <param name="Permissoes">Permissões efetivas, resolvidas pela plataforma.</param>
/// <param name="LoginsExternos">Provedores de login externo vinculados.</param>
/// <param name="DoisFatoresAtivo">Se o segundo fator está ligado.</param>
public sealed record UsuarioDetalheDto(
	Guid Id,
	string Email,
	SituacaoDoUsuario Situacao,
	DateTimeOffset? BloqueadoAte,
	IReadOnlyList<string> Perfis,
	IReadOnlyList<string> Permissoes,
	IReadOnlyList<string> LoginsExternos,
	bool DoisFatoresAtivo);
