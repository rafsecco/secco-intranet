using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants.Credenciais;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// Credenciais de um tenant administrado em memória. Toda chamada fica em <see cref="Chamadas"/> — é assim
/// que os testes provam que uma recusa não chegou à porta de escrita.
/// </summary>
public sealed class GestaoDeCredenciaisDoTenantFalsa : IGestaoDeCredenciaisDoTenant
{
	private readonly List<CredencialDto> _credenciais = [];
	private int _emitidas;

	/// <summary>Chamadas, na ordem (ex.: <c>Emitir:{tenantId}</c>).</summary>
	public List<string> Chamadas { get; } = [];

	/// <summary>Dados da última emissão aceita.</summary>
	public DadosDaCredencial? DadosEmitidos { get; private set; }

	/// <summary>Dados da última alteração aceita.</summary>
	public DadosDaCredencial? DadosAlterados { get; private set; }

	/// <summary>Quando definido, toda chamada falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Acrescenta uma credencial.</summary>
	public GestaoDeCredenciaisDoTenantFalsa ComCredencial(CredencialDto credencial)
	{
		_credenciais.Add(credencial);

		return this;
	}

	/// <inheritdoc />
	public Task<Result<IReadOnlyList<CredencialDto>>> ListarAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar<IReadOnlyList<CredencialDto>>($"Listar:{tenantId}", () => Result.Success<IReadOnlyList<CredencialDto>>([.. _credenciais])));

	/// <inheritdoc />
	public Task<Result<CredencialDto>> ObterAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"Obter:{tenantId}:{clientId}", () => Achar(clientId)));

	/// <inheritdoc />
	public Task<Result<CredencialEmitidaDto>> EmitirAsync(Guid tenantId, DadosDaCredencial dados, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"Emitir:{tenantId}", () =>
		{
			DadosEmitidos = dados;
			_emitidas++;
			var credencial = new CredencialDto(
				"cli_" + new string((char)('a' + (_emitidas % 26)), 16),
				dados.Nome, dados.Tipo, dados.Escopos, dados.Perfis, dados.RedirectUris, dados.RedirectUrisDeLogout, DateTimeOffset.UnixEpoch);
			_credenciais.Add(credencial);

			return Result.Success(new CredencialEmitidaDto(credencial, new SecretDaCredencial(credencial.ClientId, "secret-de-teste-" + _emitidas)));
		}));

	/// <inheritdoc />
	public Task<Result> AlterarAsync(Guid tenantId, string clientId, DadosDaCredencial dados, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"Alterar:{tenantId}:{clientId}", () =>
		{
			DadosAlterados = dados;

			return Result.Success();
		}));

	/// <inheritdoc />
	public Task<Result<SecretDaCredencial>> RotacionarSecretAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"Rotacionar:{tenantId}:{clientId}", () =>
			Result.Success(new SecretDaCredencial(clientId, "secret-de-teste-rotacionado"))));

	/// <inheritdoc />
	public Task<Result> RevogarAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Registrar($"Revogar:{tenantId}:{clientId}", () =>
		{
			_credenciais.RemoveAll(c => c.ClientId == clientId);

			return Result.Success();
		}));

	private Result<CredencialDto> Achar(string clientId) =>
		_credenciais.FirstOrDefault(c => c.ClientId == clientId) is { } c
			? Result.Success(c)
			: Result.Failure<CredencialDto>(IntranetErrors.Credenciais.NaoEncontrada);

	private Result<T> Registrar<T>(string chamada, Func<Result<T>> valor)
	{
		Chamadas.Add(chamada);

		return FalharCom is { } erro ? Result.Failure<T>(erro) : valor();
	}

	private Result Registrar(string chamada, Func<Result> valor)
	{
		Chamadas.Add(chamada);

		return FalharCom is { } erro ? Result.Failure(erro) : valor();
	}
}
