using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants;
using Secco.Intranet.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// API de tenants em memória. Toda chamada fica em <see cref="Chamadas"/>; criar e provisionar
/// mudam o que as leituras seguintes devolvem, como na plataforma.
/// </summary>
public sealed class GestaoDeTenantsFalsa : IGestaoDeTenants
{
	/// <summary>Tenants da instalação, com os produtos que têm banco.</summary>
	public List<TenantDaPlataformaDetalheDto> Tenants { get; } = [];

	/// <summary>Bancos que não respondem: (tenant, produto) → motivo.</summary>
	public Dictionary<(Guid, string), string> BancosSemResponder { get; } = [];

	/// <summary>Chamadas, na ordem (ex.: <c>provisionar:{id}:logstream</c>).</summary>
	public List<string> Chamadas { get; } = [];

	/// <summary>Quando definido, toda chamada falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Quando <c>true</c>, provisionar devolve script em vez de aplicar.</summary>
	public bool ModoScript { get; set; }

	/// <summary>Script devolvido em <see cref="ModoScript"/> — com uma senha falsa reconhecível.</summary>
	public const string Script = "CREATE LOGIN [x] WITH PASSWORD = 'SENHA-SECRETA-DE-TESTE';";

	/// <summary>Acrescenta um tenant.</summary>
	public GestaoDeTenantsFalsa ComTenant(Guid id, string slug, bool ativo = true, params string[] produtos)
	{
		Tenants.Add(new TenantDaPlataformaDetalheDto(id, $"Tenant {slug}", slug, ativo, produtos));

		return this;
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<TenantDaPlataformaDto>>> ListarTenantsAsync(CancellationToken cancellationToken = default)
	{
		Chamadas.Add("listar");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<IReadOnlyList<TenantDaPlataformaDto>>(erro));
		}

		IReadOnlyList<TenantDaPlataformaDto> lista = [.. Tenants.Select(t => new TenantDaPlataformaDto(t.Id, t.Nome, t.Slug, t.Ativo))];

		return Task.FromResult(Result.Success(lista));
	}

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDetalheDto>> ObterTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		Chamadas.Add($"obter:{tenantId}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<TenantDaPlataformaDetalheDto>(erro));
		}

		var tenant = Tenants.FirstOrDefault(t => t.Id == tenantId);

		return Task.FromResult(tenant is null
			? Result.Failure<TenantDaPlataformaDetalheDto>(IntranetErrors.Tenants.NaoEncontrado)
			: Result.Success(tenant));
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<StatusDoBancoDto>>> ObterStatusDosBancosAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		Chamadas.Add($"status:{tenantId}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<IReadOnlyList<StatusDoBancoDto>>(erro));
		}

		var tenant = Tenants.FirstOrDefault(t => t.Id == tenantId);
		IReadOnlyList<StatusDoBancoDto> status = tenant is null
			? []
			: [.. tenant.Produtos.Select(p => BancosSemResponder.TryGetValue((tenantId, p), out var motivo)
				? new StatusDoBancoDto(p, false, motivo)
				: new StatusDoBancoDto(p, true, null))];

		return Task.FromResult(Result.Success(status));
	}

	/// <inheritdoc />
	public Task<Result<TenantDaPlataformaDto>> CriarTenantAsync(string nome, string slug, CancellationToken cancellationToken = default)
	{
		Chamadas.Add($"criar:{slug}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<TenantDaPlataformaDto>(erro));
		}

		if (Tenants.Any(t => t.Slug == slug))
		{
			return Task.FromResult(Result.Failure<TenantDaPlataformaDto>(IntranetErrors.Tenants.SlugJaExiste));
		}

		var novo = new TenantDaPlataformaDetalheDto(Guid.NewGuid(), nome, slug, true, []);
		Tenants.Add(novo);

		return Task.FromResult(Result.Success(new TenantDaPlataformaDto(novo.Id, novo.Nome, novo.Slug, novo.Ativo)));
	}

	/// <inheritdoc />
	public Task<Result<ProvisionamentoDto>> ProvisionarAsync(Guid tenantId, string produto, CancellationToken cancellationToken = default)
	{
		Chamadas.Add($"provisionar:{tenantId}:{produto}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure<ProvisionamentoDto>(erro));
		}

		var indice = Tenants.FindIndex(t => t.Id == tenantId);

		if (indice < 0)
		{
			return Task.FromResult(Result.Failure<ProvisionamentoDto>(IntranetErrors.Tenants.NaoEncontrado));
		}

		Tenants[indice] = Tenants[indice] with { Produtos = [.. Tenants[indice].Produtos, produto] };

		return Task.FromResult(Result.Success(ModoScript ? new ProvisionamentoDto(false, Script) : new ProvisionamentoDto(true, null)));
	}

	/// <inheritdoc />
	public Task<Result> AtivarAsync(Guid tenantId, CancellationToken cancellationToken = default) => Situacao(tenantId, true);

	/// <inheritdoc />
	public Task<Result> DesativarAsync(Guid tenantId, CancellationToken cancellationToken = default) => Situacao(tenantId, false);

	private Task<Result> Situacao(Guid tenantId, bool ativo)
	{
		Chamadas.Add($"{(ativo ? "ativar" : "desativar")}:{tenantId}");

		if (FalharCom is { } erro)
		{
			return Task.FromResult(Result.Failure(erro));
		}

		var indice = Tenants.FindIndex(t => t.Id == tenantId);

		if (indice < 0)
		{
			return Task.FromResult(Result.Failure(IntranetErrors.Tenants.NaoEncontrado));
		}

		Tenants[indice] = Tenants[indice] with { Ativo = ativo };

		return Task.FromResult(Result.Success());
	}
}

/// <summary>Cadastro local em memória. <see cref="FalharAoAdicionar"/> simula o banco fora do ar.</summary>
public sealed class TenantsAdministradosFalso : ITenantsAdministrados
{
	/// <summary>Registros.</summary>
	public List<TenantAdministrado> Registros { get; } = [];

	/// <summary>Quando <c>true</c>, <see cref="TentarAdicionarAsync"/> lança, como um banco fora do ar.</summary>
	public bool FalharAoAdicionar { get; set; }

	/// <summary>Quantas vezes <see cref="SalvarAsync"/> foi chamado.</summary>
	public int Salvamentos { get; private set; }

	/// <summary>Acrescenta um registro.</summary>
	public TenantsAdministradosFalso Com(Guid tenantId, string sistema = "Sistema de compras", bool secureGate = false)
	{
		var registro = new TenantAdministrado(tenantId, sistema, "Ana", OrigemDoTenant.Criado, "admin@exemplo.com");

		if (secureGate)
		{
			registro.HabilitarSecureGate();
		}

		Registros.Add(registro);

		return this;
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<TenantAdministrado>> ListarAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<TenantAdministrado>>([.. Registros.OrderBy(r => r.Sistema)]);

	/// <inheritdoc />
	public Task<TenantAdministrado?> ObterAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registros.FirstOrDefault(r => r.TenantId == tenantId));

	/// <inheritdoc />
	public Task<TenantAdministrado?> ObterParaEdicaoAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		ObterAsync(tenantId, cancellationToken);

	/// <inheritdoc />
	public Task<bool> TentarAdicionarAsync(TenantAdministrado tenant, CancellationToken cancellationToken = default)
	{
		if (FalharAoAdicionar)
		{
			throw new InvalidOperationException("banco fora do ar (simulado)");
		}

		if (Registros.Any(r => r.TenantId == tenant.TenantId))
		{
			return Task.FromResult(false);
		}

		Registros.Add(tenant);

		return Task.FromResult(true);
	}

	/// <inheritdoc />
	public Task SalvarAsync(CancellationToken cancellationToken = default)
	{
		Salvamentos++;

		return Task.CompletedTask;
	}
}

/// <summary>Lista fixa de tenants protegidos.</summary>
/// <param name="protegidos">Ids protegidos.</param>
public sealed class TenantsProtegidosFixos(params Guid[] protegidos) : ITenantsProtegidos
{
	/// <inheritdoc />
	public Task<IReadOnlySet<Guid>> ListarAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlySet<Guid>>(protegidos.ToHashSet());
}
