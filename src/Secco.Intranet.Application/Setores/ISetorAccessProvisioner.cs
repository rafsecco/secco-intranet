using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Setores;

/// <summary>
/// Porta de provisionamento de acesso no Secco.SecureGate (ADR-0001): cada setor cadastrado
/// precisa das Roles tenant-scoped <c>{slug}-admin</c> e <c>{slug}-user</c> para que vincular
/// um usuário ao setor seja possível (via <c>UserRole</c> do SecureGate) — a Intranet não
/// modela esse vínculo em tabela própria.
/// </summary>
public interface ISetorAccessProvisioner
{
	/// <summary>
	/// Garante que as Roles <c>{slug}-admin</c> e <c>{slug}-user</c> existem no tenant atual —
	/// operação idempotente (ADR-0001): roles já existentes não são recriadas nem causam falha.
	/// </summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> EnsureSetorRolesAsync(string slug, CancellationToken cancellationToken = default);
}
