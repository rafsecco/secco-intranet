namespace Secco.Intranet.Application.Tenants.Credenciais;

/// <summary>Opções das credenciais de tenant.</summary>
public sealed class CredenciaisOptions
{
	/// <summary>Aceita endereço de retorno <c>http://</c>. A Web liga só em Development.</summary>
	public bool PermitirEnderecoHttp { get; init; }
}
