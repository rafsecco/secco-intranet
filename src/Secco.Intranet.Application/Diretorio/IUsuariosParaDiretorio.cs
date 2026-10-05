using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Usuário ativo do tenant, como o diretório precisa dele — só identidade.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição (o <c>displayName</c> do SecureGate); nulo quando não definido.</param>
public sealed record UsuarioParaDiretorio(Guid Id, string Email, string? Nome = null);

/// <summary>
/// Fonte de identidade do diretório: os usuários <b>ativos</b> do tenant atual. Falha de
/// infraestrutura volta como <see cref="Result"/> — uma lista vazia silenciosa faria o diretório
/// parecer vazio quando o SecureGate só está fora do ar.
/// </summary>
public interface IUsuariosParaDiretorio
{
	/// <summary>Lista os usuários ativos do tenant atual.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Descarta a lista guardada em cache, para a próxima leitura ir à fonte — chamado depois de
	/// gravar um nome, para a pessoa ver a mudança na hora. Adaptadores sem cache não fazem nada.
	/// </summary>
	void Esquecer();
}
