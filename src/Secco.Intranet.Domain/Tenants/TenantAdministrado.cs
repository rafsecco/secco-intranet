using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Tenants;

/// <summary>Como o tenant entrou no cadastro da Intranet.</summary>
public enum OrigemDoTenant
{
	/// <summary>Criado pela própria Intranet.</summary>
	Criado = 0,

	/// <summary>Já existia no SecureGate e foi adotado explicitamente.</summary>
	Adotado = 1,
}

/// <summary>
/// Tenant de outro sistema da empresa que esta Intranet administra (ADR-0008). É a lista de
/// permissão da área de tenants: rota com um tenant fora daqui não existe. Nome, slug e situação
/// não são copiados — vêm do SecureGate a cada leitura, para não divergir.
/// </summary>
public sealed class TenantAdministrado : BaseEntity
{
	/// <summary>Tamanho máximo do nome do sistema.</summary>
	public const int SistemaMaxLength = 120;

	/// <summary>Tamanho máximo do responsável.</summary>
	public const int ResponsavelMaxLength = 120;

	/// <summary>Tamanho máximo do rótulo de quem registrou.</summary>
	public const int RegistradoPorMaxLength = 200;

	private TenantAdministrado()
	{
		// Construtor de rehidratação do EF Core
		Sistema = string.Empty;
		Responsavel = string.Empty;
		RegistradoPor = string.Empty;
	}

	/// <summary>Registra um tenant administrado.</summary>
	/// <param name="tenantId">Id do tenant no SecureGate. Obrigatório.</param>
	/// <param name="sistema">Sistema que o tenant representa. Obrigatório.</param>
	/// <param name="responsavel">Responsável pelo sistema, texto livre. Obrigatório.</param>
	/// <param name="origem">Criado aqui ou adotado.</param>
	/// <param name="registradoPor">Rótulo de quem registrou (o mesmo da trilha); truncado no limite.</param>
	/// <exception cref="DomainInvariantException">Se algum argumento obrigatório faltar ou exceder o limite.</exception>
	public TenantAdministrado(Guid tenantId, string sistema, string responsavel, OrigemDoTenant origem, string registradoPor)
	{
		if (tenantId == Guid.Empty)
		{
			throw new DomainInvariantException("Um tenant administrado exige o id do tenant.");
		}

		Sistema = Obrigatorio(sistema, SistemaMaxLength, "sistema");
		Responsavel = Obrigatorio(responsavel, ResponsavelMaxLength, "responsável");
		TenantId = tenantId;
		Origem = origem;

		var rotulo = string.IsNullOrWhiteSpace(registradoPor) ? "desconhecido" : registradoPor.Trim();
		RegistradoPor = rotulo.Length > RegistradoPorMaxLength ? rotulo[..RegistradoPorMaxLength] : rotulo;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Id do tenant no SecureGate (coluna <c>tenant_id</c>, único).</summary>
	public Guid TenantId { get; private set; }

	/// <summary>Sistema que o tenant representa (coluna <c>ds_sistema</c>).</summary>
	public string Sistema { get; private set; }

	/// <summary>Responsável pelo sistema (coluna <c>ds_responsavel</c>).</summary>
	public string Responsavel { get; private set; }

	/// <summary>Criado aqui ou adotado (coluna <c>ie_origem</c>).</summary>
	public OrigemDoTenant Origem { get; private set; }

	/// <summary>
	/// Gestão de perfis e usuários deste tenant habilitada na Intranet (coluna
	/// <c>fl_secure_gate_habilitado</c>). É a única marca local de recurso: o SecureGate não tem
	/// banco por tenant, então "ligado" é decisão da Intranet, não estado da plataforma.
	/// </summary>
	public bool SecureGateHabilitado { get; private set; }

	/// <summary>Quem registrou (coluna <c>ds_registrado_por</c>).</summary>
	public string RegistradoPor { get; private set; }

	/// <summary>Quando foi registrado (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Última alteração (coluna <c>dt_updated_at</c>).</summary>
	public DateTimeOffset? UpdatedAt { get; private set; }

	/// <summary>Habilita a gestão de perfis e usuários deste tenant.</summary>
	/// <returns><c>false</c> se já estava habilitada — nada muda.</returns>
	public bool HabilitarSecureGate()
	{
		if (SecureGateHabilitado)
		{
			return false;
		}

		SecureGateHabilitado = true;
		UpdatedAt = DateTimeOffset.UtcNow;

		return true;
	}

	private static string Obrigatorio(string? valor, int limite, string campo)
	{
		var aparado = valor?.Trim() ?? string.Empty;

		if (aparado.Length == 0)
		{
			throw new DomainInvariantException($"O {campo} é obrigatório.");
		}

		if (aparado.Length > limite)
		{
			throw new DomainInvariantException($"O {campo} excede {limite} caracteres.");
		}

		return aparado;
	}
}
