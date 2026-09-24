using Secco.Intranet.Application;
using Secco.Intranet.Application.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// No-op para ambientes sem SecureGate e sem modo de desenvolvimento (Testing, produção sem a
/// seção). Responde "não configurado": as telas explicam, em vez de mostrar um diretório vazio.
/// </summary>
public sealed class UsuariosParaDiretorioIndisponivel : IUsuariosParaDiretorio
{
	/// <inheritdoc />
	public Task<Result<IReadOnlyList<UsuarioParaDiretorio>>> ListarAtivosAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(Result.Failure<IReadOnlyList<UsuarioParaDiretorio>>(IntranetErrors.Acesso.NaoConfigurado));
}
