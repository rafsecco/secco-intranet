using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Infrastructure.Notificacao;

/// <summary>
/// Adapter no-op de <see cref="IDiretorioDeUsuarios"/> — sem SecureGate configurado não há
/// cadastro a consultar, e ninguém é destinatário.
/// </summary>
public sealed class DiretorioVazio : IDiretorioDeUsuarios
{
	/// <inheritdoc />
	public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
		CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<UsuarioDoTenant>>([]);
}
