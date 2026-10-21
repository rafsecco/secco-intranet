namespace Secco.Intranet.Application.Tenants.Credenciais;

/// <summary>Tipo da credencial OAuth de um tenant administrado.</summary>
public enum TipoDeCredencial
{
	/// <summary>Sistema falando com sistema (client credentials), sem pessoa.</summary>
	Maquina,

	/// <summary>Sistema que entra pessoas pelo SecureGate (authorization code).</summary>
	Login,
}

/// <summary>Uma credencial, sem o secret.</summary>
/// <param name="ClientId">Identificador público, no formato <c>cli_</c> + 16 caracteres base32.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Tipo">Máquina ou login.</param>
/// <param name="Escopos">Produtos que a credencial alcança.</param>
/// <param name="Perfis">Perfis que a credencial de máquina assume.</param>
/// <param name="RedirectUris">Endereços de retorno do login.</param>
/// <param name="RedirectUrisDeLogout">Endereços de retorno do logout.</param>
/// <param name="CriadaEm">Quando foi emitida, se a plataforma informa.</param>
public sealed record CredencialDto(
	string ClientId,
	string Nome,
	TipoDeCredencial Tipo,
	IReadOnlyList<string> Escopos,
	IReadOnlyList<string> Perfis,
	IReadOnlyList<string> RedirectUris,
	IReadOnlyList<string> RedirectUrisDeLogout,
	DateTimeOffset? CriadaEm);

/// <summary>O que o admin informa para emitir ou alterar uma credencial.</summary>
/// <param name="Tipo">Máquina ou login.</param>
/// <param name="Nome">Nome de exibição, já aparado.</param>
/// <param name="Escopos">Produtos escolhidos.</param>
/// <param name="Perfis">Perfis (só de máquina).</param>
/// <param name="RedirectUris">Endereços de retorno do login (só de login).</param>
/// <param name="RedirectUrisDeLogout">Endereços de retorno do logout (só de login).</param>
public sealed record DadosDaCredencial(
	TipoDeCredencial Tipo,
	string Nome,
	IReadOnlyList<string> Escopos,
	IReadOnlyList<string> Perfis,
	IReadOnlyList<string> RedirectUris,
	IReadOnlyList<string> RedirectUrisDeLogout);

/// <summary>
/// Secret de uma credencial. Classe, e não record: o <c>ToString()</c> gerado por um record
/// listaria o valor, e ele não pode ir parar em log nem em mensagem de exceção.
/// </summary>
public sealed class SecretDaCredencial
{
	/// <summary>Cria o secret de uma credencial.</summary>
	/// <param name="clientId">Credencial a que o secret pertence.</param>
	/// <param name="valor">Valor em claro, mostrado uma única vez.</param>
	public SecretDaCredencial(string clientId, string valor)
	{
		ClientId = clientId;
		Valor = valor;
	}

	/// <summary>Credencial a que o secret pertence.</summary>
	public string ClientId { get; }

	/// <summary>Valor em claro. Nunca vai para log, trilha, banco, cookie ou TempData.</summary>
	public string Valor { get; }

	/// <inheritdoc />
	public override string ToString() => $"SecretDaCredencial {{ ClientId = {ClientId} }}";
}

/// <summary>Resultado de emitir uma credencial: ela e o secret, que só existe agora.</summary>
/// <param name="Credencial">A credencial emitida.</param>
/// <param name="Secret">O secret em claro.</param>
public sealed record CredencialEmitidaDto(CredencialDto Credencial, SecretDaCredencial Secret);
