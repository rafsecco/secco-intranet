using Secco.SDK.AspNetCore.Authorization;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// Dublê de <see cref="IPermissionResolver"/>: ignora tenant (os testes deste produto não
/// precisam distinguir tenant para permissão) e resolve só pelo nome da Role, configurado à mão
/// pelo teste. Sem nenhuma permissão configurada para a Role, devolve vazio — nunca lança.
/// </summary>
public sealed class PermissionResolverDeTeste : IPermissionResolver
{
	private readonly Dictionary<string, HashSet<string>> _permissoesPorRole = new(StringComparer.Ordinal);

	/// <summary>Registra uma permissão para uma Role. Encadeável.</summary>
	public PermissionResolverDeTeste ComPermissao(string role, string permissao)
	{
		if (!_permissoesPorRole.TryGetValue(role, out var permissoes))
		{
			permissoes = new HashSet<string>(StringComparer.Ordinal);
			_permissoesPorRole[role] = permissoes;
		}

		permissoes.Add(permissao);

		return this;
	}

	/// <inheritdoc />
	public ValueTask<IReadOnlySet<string>> ResolveAsync(Guid tenantId, string role, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlySet<string>>(
			_permissoesPorRole.TryGetValue(role, out var permissoes) ? permissoes : new HashSet<string>());
}

/// <summary>Dublê que sempre lança — prova que resolução indisponível vira negação, não 500.</summary>
public sealed class PermissionResolverQueLanca : IPermissionResolver
{
	/// <inheritdoc />
	public ValueTask<IReadOnlySet<string>> ResolveAsync(Guid tenantId, string role, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException("Resolvedor de permissao indisponivel (dublê de teste).");
}
