using Secco.Intranet.Application.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// Adaptador de DEV: expõe as pessoas fictícias como usuários do tenant. Só é escolhido em
/// <c>Development</c> <b>e</b> sem SecureGate configurado — nunca serve gente inventada numa
/// intranet em uso, que era o risco que o flag de demonstração existia para conter.
/// </summary>
public sealed class UsuariosParaDiretorioDeDesenvolvimento : IUsuariosParaDiretorio
{
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<UsuarioParaDiretorio> usuarios =
			[.. PessoasDeDesenvolvimento.Todas.Select(pessoa => new UsuarioParaDiretorio(pessoa.Id, pessoa.Email))];

		return Task.FromResult(Result.Success(usuarios));
	}
}
