using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Credenciais;

/// <summary>O que o admin preencheu no formulário de uma credencial. As URIs vêm em texto, uma por linha.</summary>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Escopos">Produtos marcados.</param>
/// <param name="Perfis">Perfis marcados (só de máquina).</param>
/// <param name="RedirectUris">Endereços de retorno do login, um por linha.</param>
/// <param name="RedirectUrisDeLogout">Endereços de retorno do logout, um por linha.</param>
public sealed record FormularioDaCredencial(
	string? Nome,
	IReadOnlyList<string>? Escopos,
	IReadOnlyList<string>? Perfis,
	string? RedirectUris,
	string? RedirectUrisDeLogout);

/// <summary>
/// Escritas das credenciais de um tenant administrado: emitir, alterar, rotacionar e revogar. Valida
/// as regras antes da plataforma e só registra na trilha depois que ela aceitou. O secret nunca vai à trilha.
/// </summary>
/// <param name="gestao">Porta das credenciais.</param>
/// <param name="leitura">Leitura (credencial e opções).</param>
/// <param name="opcoes">Opções das credenciais.</param>
/// <param name="trilha">Trilha da Intranet.</param>
public sealed class CredenciaisDoTenantHandler(
	IGestaoDeCredenciaisDoTenant gestao, LeituraDeCredenciaisHandler leitura, CredenciaisOptions opcoes, ITrilhaDeAuditoria trilha)
{
	/// <summary>Emite uma credencial. O secret volta só aqui.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="tipo">Máquina ou login.</param>
	/// <param name="formulario">Dados do formulário.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	public async Task<Result<CredencialEmitidaDto>> EmitirAsync(
		Guid tenantId, TipoDeCredencial tipo, FormularioDaCredencial formulario, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(formulario);

		var dados = await ValidarAsync(tenantId, tipo, formulario, cancellationToken).ConfigureAwait(false);

		if (dados.IsFailure)
		{
			return Result.Failure<CredencialEmitidaDto>(dados.Error);
		}

		var emitida = await gestao.EmitirAsync(tenantId, dados.Value, cancellationToken).ConfigureAwait(false);

		if (emitida.IsSuccess)
		{
			await AuditoriaDeCredenciais.EmitidaAsync(trilha, tenantId, emitida.Value.Credencial.ClientId, dados.Value, cancellationToken)
				.ConfigureAwait(false);
		}

		return emitida;
	}

	/// <summary>Altera uma credencial. O tipo é o da credencial lida, não o do formulário.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="clientId">Credencial.</param>
	/// <param name="formulario">Dados do formulário.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	public async Task<Result> AlterarAsync(
		Guid tenantId, string? clientId, FormularioDaCredencial formulario, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(formulario);

		var atual = await leitura.ObterAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false);

		if (atual.IsFailure)
		{
			return Result.Failure(atual.Error);
		}

		var dados = await ValidarAsync(tenantId, atual.Value.Tipo, formulario, cancellationToken).ConfigureAwait(false);

		if (dados.IsFailure)
		{
			return Result.Failure(dados.Error);
		}

		var alterada = await gestao.AlterarAsync(tenantId, atual.Value.ClientId, dados.Value, cancellationToken).ConfigureAwait(false);

		if (alterada.IsSuccess)
		{
			await AuditoriaDeCredenciais.AlteradaAsync(trilha, tenantId, atual.Value.ClientId, dados.Value, cancellationToken)
				.ConfigureAwait(false);
		}

		return alterada;
	}

	/// <summary>Troca o secret da credencial. O novo volta só aqui.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="clientId">Credencial.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	public async Task<Result<SecretDaCredencial>> RotacionarAsync(Guid tenantId, string? clientId, CancellationToken cancellationToken = default)
	{
		var atual = await leitura.ObterAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false);

		if (atual.IsFailure)
		{
			return Result.Failure<SecretDaCredencial>(atual.Error);
		}

		var rotacionada = await gestao.RotacionarSecretAsync(tenantId, atual.Value.ClientId, cancellationToken).ConfigureAwait(false);

		if (rotacionada.IsSuccess)
		{
			await AuditoriaDeCredenciais.RotacionadaAsync(trilha, tenantId, atual.Value.ClientId, cancellationToken).ConfigureAwait(false);
		}

		return rotacionada;
	}

	/// <summary>Revoga a credencial, se a confirmação for o nome exato dela.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="clientId">Credencial.</param>
	/// <param name="confirmacao">Nome digitado pelo admin.</param>
	/// <param name="cancellationToken">Cancelamento.</param>
	public async Task<Result> RevogarAsync(Guid tenantId, string? clientId, string? confirmacao, CancellationToken cancellationToken = default)
	{
		var atual = await leitura.ObterAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false);

		if (atual.IsFailure)
		{
			return Result.Failure(atual.Error);
		}

		if (!string.Equals(confirmacao?.Trim(), atual.Value.Nome, StringComparison.Ordinal))
		{
			return Result.Failure(IntranetErrors.Credenciais.ConfirmacaoNaoConfere);
		}

		var revogada = await gestao.RevogarAsync(tenantId, atual.Value.ClientId, cancellationToken).ConfigureAwait(false);

		if (revogada.IsSuccess)
		{
			await AuditoriaDeCredenciais.RevogadaAsync(trilha, tenantId, atual.Value.ClientId, atual.Value.Nome, cancellationToken)
				.ConfigureAwait(false);
		}

		return revogada;
	}

	private async Task<Result<DadosDaCredencial>> ValidarAsync(
		Guid tenantId, TipoDeCredencial tipo, FormularioDaCredencial formulario, CancellationToken cancellationToken)
	{
		var contexto = await leitura.OpcoesAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (contexto.IsFailure)
		{
			return Result.Failure<DadosDaCredencial>(contexto.Error);
		}

		var erro = Validar(tipo, formulario, contexto.Value, out var dados);

		return erro is { } e ? Result.Failure<DadosDaCredencial>(e) : Result.Success(dados);
	}

	private Error? Validar(TipoDeCredencial tipo, FormularioDaCredencial f, OpcoesDaCredencial contexto, out DadosDaCredencial dados)
	{
		dados = new DadosDaCredencial(tipo, string.Empty, [], [], [], []);

		var nome = f.Nome?.Trim() ?? string.Empty;

		if (!RegrasDeCredencial.NomeValido(nome))
		{
			return IntranetErrors.Credenciais.NomeInvalido;
		}

		var escopos = Distintos(f.Escopos);

		var recusado = escopos.FirstOrDefault(e => !contexto.EscoposDisponiveis.Contains(e, StringComparer.Ordinal));

		if (recusado is not null)
		{
			return IntranetErrors.Credenciais.EscopoNaoLigado(
				RecursosDaPlataforma.TentarLer(recusado, out var recurso) ? RecursosDaPlataforma.Nome(recurso) : recusado);
		}

		if (tipo == TipoDeCredencial.Maquina && escopos.Count == 0)
		{
			return IntranetErrors.Credenciais.SemEscopo;
		}

		var perfis = Distintos(f.Perfis);
		var redirects = RegrasDeCredencial.Linhas(f.RedirectUris);
		var logouts = RegrasDeCredencial.Linhas(f.RedirectUrisDeLogout);

		var erro = tipo == TipoDeCredencial.Login
			? ValidarLogin(contexto, perfis, redirects, logouts)
			: ValidarMaquina(contexto, perfis, redirects, logouts);

		if (erro is not null)
		{
			return erro;
		}

		dados = new DadosDaCredencial(tipo, nome, escopos, perfis, redirects, logouts);

		return null;
	}

	private Error? ValidarLogin(
		OpcoesDaCredencial contexto, IReadOnlyList<string> perfis, IReadOnlyList<string> redirects, IReadOnlyList<string> logouts)
	{
		if (!contexto.SecureGateLigado)
		{
			return IntranetErrors.Credenciais.LoginSemSecureGate;
		}

		if (perfis.Count > 0)
		{
			return IntranetErrors.Credenciais.LoginComPerfis;
		}

		if (redirects.Count == 0)
		{
			return IntranetErrors.Credenciais.LoginSemEndereco;
		}

		if (redirects.Count > RegrasDeCredencial.EnderecosMaximo || logouts.Count > RegrasDeCredencial.EnderecosMaximo)
		{
			return IntranetErrors.Credenciais.LimiteExcedido;
		}

		return redirects.Concat(logouts).Any(u => !RegrasDeCredencial.EnderecoValido(u, opcoes.PermitirEnderecoHttp))
			? IntranetErrors.Credenciais.EnderecoRecusado
			: null;
	}

	private static Error? ValidarMaquina(
		OpcoesDaCredencial contexto, IReadOnlyList<string> perfis, IReadOnlyList<string> redirects, IReadOnlyList<string> logouts)
	{
		if (redirects.Count > 0 || logouts.Count > 0)
		{
			return IntranetErrors.Credenciais.MaquinaComEndereco;
		}

		if (perfis.Count > RegrasDeCredencial.PerfisMaximo)
		{
			return IntranetErrors.Credenciais.LimiteExcedido;
		}

		if (perfis.Count == 0)
		{
			return null;
		}

		if (!contexto.SecureGateLigado)
		{
			return IntranetErrors.Credenciais.PerfisSemSecureGate;
		}

		return perfis.Any(p => !contexto.PerfisDisponiveis.Any(d => string.Equals(d.Nome, p, StringComparison.Ordinal)))
			? IntranetErrors.Credenciais.PerfilRecusado
			: null;
	}

	private static List<string> Distintos(IReadOnlyList<string>? valores) =>
		[.. (valores ?? []).Distinct(StringComparer.Ordinal)];
}
