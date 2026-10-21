using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Credenciais;

/// <summary>O que o formulário de uma credencial oferece, conforme o que o tenant tem ligado.</summary>
/// <param name="Tenant">Tenant administrado (ativo e recursos).</param>
/// <param name="EscoposDisponiveis">Produtos que o tenant tem ligados.</param>
/// <param name="SecureGateLigado">Se o SecureGate está ligado (login e perfis dependem dele).</param>
/// <param name="PerfisDisponiveis">Perfis do tenant sem os reservados; vazio com o SecureGate desligado.</param>
public sealed record OpcoesDaCredencial(
	TenantAdministradoDetalheDto Tenant,
	IReadOnlyList<string> EscoposDisponiveis,
	bool SecureGateLigado,
	IReadOnlyList<PerfilDoTenantDto> PerfisDisponiveis);

/// <summary>Leitura das credenciais de um tenant administrado e das opções do formulário.</summary>
/// <param name="gestao">Porta das credenciais.</param>
/// <param name="tenant">Detalhe do tenant (recursos).</param>
/// <param name="acesso">Porta do acesso, para os perfis.</param>
public sealed class LeituraDeCredenciaisHandler(
	IGestaoDeCredenciaisDoTenant gestao, ObterTenantAdministradoHandler tenant, IGestaoDeAcessoDoTenant acesso)
{
	/// <summary>Lista as credenciais do tenant.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	public Task<Result<IReadOnlyList<CredencialDto>>> ListarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		gestao.ListarAsync(tenantId, cancellationToken);

	/// <summary>Obtém uma credencial. Client id fora do formato é <c>NaoEncontrada</c>, sem chamar a porta.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="clientId">Client id vindo da rota.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	public Task<Result<CredencialDto>> ObterAsync(Guid tenantId, string? clientId, CancellationToken cancellationToken = default) =>
		RegrasDeCredencial.ClientIdValido(clientId)
			? gestao.ObterAsync(tenantId, clientId!, cancellationToken)
			: Task.FromResult(Result.Failure<CredencialDto>(IntranetErrors.Credenciais.NaoEncontrada));

	/// <summary>Opções do formulário: escopos ligados, SecureGate e perfis atribuíveis.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	public async Task<Result<OpcoesDaCredencial>> OpcoesAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var detalhe = await tenant.HandleAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (detalhe.IsFailure)
		{
			return Result.Failure<OpcoesDaCredencial>(detalhe.Error);
		}

		var secureGate = RegrasDeCredencial.SecureGateLigado(detalhe.Value.Recursos);
		IReadOnlyList<PerfilDoTenantDto> perfis = [];

		if (secureGate)
		{
			var lista = await acesso.ListarPerfisAsync(tenantId, cancellationToken).ConfigureAwait(false);

			if (lista.IsFailure)
			{
				return Result.Failure<OpcoesDaCredencial>(lista.Error);
			}

			perfis = [.. lista.Value.Where(p => !p.Reservado && !ClassificacaoDePerfil.EhReservado(p.Nome))];
		}

		return Result.Success(new OpcoesDaCredencial(
			detalhe.Value, RegrasDeCredencial.EscoposLigados(detalhe.Value.Recursos), secureGate, perfis));
	}
}
