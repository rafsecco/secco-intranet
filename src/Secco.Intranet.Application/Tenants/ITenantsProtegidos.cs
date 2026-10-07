namespace Secco.Intranet.Application.Tenants;

/// <summary>
/// Tenants que esta Intranet nunca administra, qualquer que seja o pedido: o de instalação da
/// plataforma, o da própria requisição e todo tenant que é uma Intranet (catálogo do produto).
/// A última regra impede a filial A de adotar a filial B na mesma instalação.
/// </summary>
public interface ITenantsProtegidos
{
	/// <summary>Ids protegidos.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlySet<Guid>> ListarAsync(CancellationToken cancellationToken = default);
}
