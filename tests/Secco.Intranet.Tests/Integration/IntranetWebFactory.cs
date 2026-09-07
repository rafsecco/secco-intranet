using Secco.Intranet.Infrastructure;
using Secco.SDK.Testing;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Sobe o monolito real (<c>Secco.Intranet.Web</c>, ADR-0002) sobre a base de factories da
/// plataforma (ADR-0027), com dois tenants apontando para bancos distintos (ADR-0005).
/// </summary>
/// <remarks>
/// <para>
/// Herdar de <see cref="SeccoApiFactory{TProgram}"/> traz a instância de SQL Server — container
/// próprio ou servidor externo via <c>SECCO_TEST_SQLSERVER</c>, com sufixo de execução por
/// instância —, a trava de migration e a montagem do catálogo de tenants. A ADR-0027 é explícita
/// em que produto novo herde daqui em vez de manter a própria cópia.
/// </para>
/// <para>
/// Um descasamento a registrar: a base foi desenhada para resource server JWT, e a Intranet é
/// um relying party de cookie (ADR-0023) — ela nunca chama <c>AddSeccoAuthentication()</c>.
/// Por isso o <see cref="Audience"/> abaixo e as chaves <c>Secco:Authentication:*</c> que a base
/// injeta não são lidos por ninguém aqui; existem para satisfazer o contrato da classe base.
/// Os testes deste produto não usam <c>CreateToken</c>: a autenticação não é registrada no
/// ambiente <c>Testing</c>, e o host roda em modo aberto.
/// </para>
/// </remarks>
public sealed class IntranetWebFactory : SeccoApiFactory<Program>
{
	/// <summary>
	/// Raiz do armazenamento de documentos desta instância. Sai do diretório de saída da
	/// compilação de propósito: arquivo de teste não deve sujar o bin do produto.
	/// </summary>
	private readonly string _raizDeArquivos = Path.Combine(
		Path.GetTempPath(), "secco-intranet-testes", Guid.NewGuid().ToString("N"));

	/// <summary>Identificador do tenant "Alfa" usado nos testes.</summary>
	public Guid TenantAlfa { get; } = Guid.NewGuid();

	/// <summary>Identificador do tenant "Beta" usado nos testes.</summary>
	public Guid TenantBeta { get; } = Guid.NewGuid();

	/// <inheritdoc />
	protected override string Audience => "secco-intranet";

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		AddTenant(settings, TenantAlfa, GetConnectionStringFor("secco_intranet_alfa"));
		AddTenant(settings, TenantBeta, GetConnectionStringFor("secco_intranet_beta"));

		settings["Intranet:Documentos:Armazenamento:Raiz"] = _raizDeArquivos;
	}

	/// <inheritdoc />
	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);

		if (!disposing || !Directory.Exists(_raizDeArquivos))
		{
			return;
		}

		try
		{
			Directory.Delete(_raizDeArquivos, recursive: true);
		}
		catch (IOException)
		{
			// Limpeza é best-effort: um arquivo ainda aberto não pode derrubar a suíte.
		}
		catch (UnauthorizedAccessException)
		{
			// Idem.
		}
	}

	/// <inheritdoc />
	protected override Task MigrateAsync(IServiceProvider services) =>
		services.MigrateIntranetTenantDatabasesAsync();
}
