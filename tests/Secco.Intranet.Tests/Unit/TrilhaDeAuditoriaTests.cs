using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Infrastructure.Auditoria;
using Secco.LogStream.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// O adaptador da trilha. O que importa aqui não é o formato do registro: é que ele
/// <b>nunca lance</b>. Auditar é consequência da ação do usuário, e uma falha de rede no
/// serviço de observabilidade não pode desfazer o que a pessoa acabou de fazer.
/// </summary>
public class TrilhaDeAuditoriaTests
{
	private sealed class ClientFalso : ILogStreamClient
	{
		public List<CreateAuditEntryRequest> Registros { get; } = [];

		public Task<AuditEntryDto> CreateAuditEntryAsync(
			CreateAuditEntryRequest body, CancellationToken cancellationToken)
		{
			Registros.Add(body);

			return Task.FromResult(new AuditEntryDto());
		}

		public Task<AuditEntryDto> CreateAuditEntryAsync(CreateAuditEntryRequest body) =>
			CreateAuditEntryAsync(body, CancellationToken.None);

		// ILogStreamClient tem muitos membros além dos de auditoria; o teste não usa nenhum
		// deles, então cada um vira uma linha de NotImplementedException.
		public Task<LogEntryAcceptedResponse> CreateApiCallLogAsync(CreateApiCallLogRequest body) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateApiCallLogAsync(CreateApiCallLogRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogEntryAsync(CreateLogEntryRequest body) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogEntryAsync(CreateLogEntryRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryBatchAcceptedResponse> CreateLogEntryBatchAsync(IEnumerable<CreateLogEntryRequest> body) => throw new NotImplementedException();
		public Task<LogEntryBatchAcceptedResponse> CreateLogEntryBatchAsync(IEnumerable<CreateLogEntryRequest> body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogProcessAsync(CreateLogProcessRequest body) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogProcessAsync(CreateLogProcessRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogProcessDetailAsync(Guid processId, CreateLogProcessDetailRequest body) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogProcessDetailAsync(Guid processId, CreateLogProcessDetailRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryBatchAcceptedResponse> CreateLogProcessDetailBatchAsync(Guid processId, IEnumerable<CreateLogProcessDetailRequest> body) => throw new NotImplementedException();
		public Task<LogEntryBatchAcceptedResponse> CreateLogProcessDetailBatchAsync(Guid processId, IEnumerable<CreateLogProcessDetailRequest> body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<ApiCallLogDto> GetApiCallLogAsync(Guid id) => throw new NotImplementedException();
		public Task<ApiCallLogDto> GetApiCallLogAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<AuditEntryDto> GetAuditEntryAsync(Guid id) => throw new NotImplementedException();
		public Task<AuditEntryDto> GetAuditEntryAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryDto> GetLogEntryAsync(Guid id) => throw new NotImplementedException();
		public Task<LogEntryDto> GetLogEntryAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogProcessDto> GetLogProcessAsync(Guid id) => throw new NotImplementedException();
		public Task<LogProcessDto> GetLogProcessAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfLogProcessDetailDto> GetLogProcessDetailsAsync(Guid processId, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfLogProcessDetailDto> GetLogProcessDetailsAsync(Guid processId, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfApiCallLogDto> SearchApiCallLogsAsync(DateTimeOffset? from, DateTimeOffset? to, bool? success, string method, string path, int? statusCode, Guid? correlationId, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfApiCallLogDto> SearchApiCallLogsAsync(DateTimeOffset? from, DateTimeOffset? to, bool? success, string method, string path, int? statusCode, Guid? correlationId, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfAuditEntryDto> SearchAuditEntriesAsync(DateTimeOffset? from, DateTimeOffset? to, string action, string resourceType, string resourceId, string actorId, Guid? correlationId, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfAuditEntryDto> SearchAuditEntriesAsync(DateTimeOffset? from, DateTimeOffset? to, string action, string resourceType, string resourceId, string actorId, Guid? correlationId, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfLogEntryDto> SearchLogEntriesAsync(DateTimeOffset? from, DateTimeOffset? to, LogEntryLevel? level, string source, Guid? correlationId, string search, string category, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfLogEntryDto> SearchLogEntriesAsync(DateTimeOffset? from, DateTimeOffset? to, LogEntryLevel? level, string source, Guid? correlationId, string search, string category, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfLogProcessDto> SearchLogProcessesAsync(DateTimeOffset? from, DateTimeOffset? to, string name, ProcessStatus? status, Guid? correlationId, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfLogProcessDto> SearchLogProcessesAsync(DateTimeOffset? from, DateTimeOffset? to, string name, ProcessStatus? status, Guid? correlationId, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
	}

	private sealed class ClientQueFalha : ILogStreamClient
	{
		public Task<AuditEntryDto> CreateAuditEntryAsync(
			CreateAuditEntryRequest body, CancellationToken cancellationToken) =>
			throw new HttpRequestException("LogStream fora do ar.");

		public Task<AuditEntryDto> CreateAuditEntryAsync(CreateAuditEntryRequest body) =>
			CreateAuditEntryAsync(body, CancellationToken.None);

		// Idem ClientFalso: só o de auditoria importa para este teste.
		public Task<LogEntryAcceptedResponse> CreateApiCallLogAsync(CreateApiCallLogRequest body) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateApiCallLogAsync(CreateApiCallLogRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogEntryAsync(CreateLogEntryRequest body) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogEntryAsync(CreateLogEntryRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryBatchAcceptedResponse> CreateLogEntryBatchAsync(IEnumerable<CreateLogEntryRequest> body) => throw new NotImplementedException();
		public Task<LogEntryBatchAcceptedResponse> CreateLogEntryBatchAsync(IEnumerable<CreateLogEntryRequest> body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogProcessAsync(CreateLogProcessRequest body) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogProcessAsync(CreateLogProcessRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogProcessDetailAsync(Guid processId, CreateLogProcessDetailRequest body) => throw new NotImplementedException();
		public Task<LogEntryAcceptedResponse> CreateLogProcessDetailAsync(Guid processId, CreateLogProcessDetailRequest body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryBatchAcceptedResponse> CreateLogProcessDetailBatchAsync(Guid processId, IEnumerable<CreateLogProcessDetailRequest> body) => throw new NotImplementedException();
		public Task<LogEntryBatchAcceptedResponse> CreateLogProcessDetailBatchAsync(Guid processId, IEnumerable<CreateLogProcessDetailRequest> body, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<ApiCallLogDto> GetApiCallLogAsync(Guid id) => throw new NotImplementedException();
		public Task<ApiCallLogDto> GetApiCallLogAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<AuditEntryDto> GetAuditEntryAsync(Guid id) => throw new NotImplementedException();
		public Task<AuditEntryDto> GetAuditEntryAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogEntryDto> GetLogEntryAsync(Guid id) => throw new NotImplementedException();
		public Task<LogEntryDto> GetLogEntryAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<LogProcessDto> GetLogProcessAsync(Guid id) => throw new NotImplementedException();
		public Task<LogProcessDto> GetLogProcessAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfLogProcessDetailDto> GetLogProcessDetailsAsync(Guid processId, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfLogProcessDetailDto> GetLogProcessDetailsAsync(Guid processId, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfApiCallLogDto> SearchApiCallLogsAsync(DateTimeOffset? from, DateTimeOffset? to, bool? success, string method, string path, int? statusCode, Guid? correlationId, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfApiCallLogDto> SearchApiCallLogsAsync(DateTimeOffset? from, DateTimeOffset? to, bool? success, string method, string path, int? statusCode, Guid? correlationId, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfAuditEntryDto> SearchAuditEntriesAsync(DateTimeOffset? from, DateTimeOffset? to, string action, string resourceType, string resourceId, string actorId, Guid? correlationId, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfAuditEntryDto> SearchAuditEntriesAsync(DateTimeOffset? from, DateTimeOffset? to, string action, string resourceType, string resourceId, string actorId, Guid? correlationId, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfLogEntryDto> SearchLogEntriesAsync(DateTimeOffset? from, DateTimeOffset? to, LogEntryLevel? level, string source, Guid? correlationId, string search, string category, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfLogEntryDto> SearchLogEntriesAsync(DateTimeOffset? from, DateTimeOffset? to, LogEntryLevel? level, string source, Guid? correlationId, string search, string category, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
		public Task<PagedResultOfLogProcessDto> SearchLogProcessesAsync(DateTimeOffset? from, DateTimeOffset? to, string name, ProcessStatus? status, Guid? correlationId, int? skip, int? take) => throw new NotImplementedException();
		public Task<PagedResultOfLogProcessDto> SearchLogProcessesAsync(DateTimeOffset? from, DateTimeOffset? to, string name, ProcessStatus? status, Guid? correlationId, int? skip, int? take, CancellationToken cancellationToken) => throw new NotImplementedException();
	}

	private sealed class AtorFalso(AtorDaAcao? ator) : IAtorAtual
	{
		public AtorDaAcao? Atual() => ator;
	}

	private static readonly AtorDaAcao Alguem = new("018f0000-0000-7000-8000-000000000009", "Ana");

	private static RegistroDeAuditoria Registro() =>
		new(VerbosDeAuditoria.MuralPublicar, RecursosDeAuditoria.Publicacao, "abc-123", """{"titulo":"x"}""");

	[Fact]
	public async Task ComAtor_EnviaVerboRecursoEId()
	{
		var client = new ClientFalso();
		var trilha = new LogStreamTrilhaDeAuditoria(
			client, new AtorFalso(Alguem), NullLogger<LogStreamTrilhaDeAuditoria>.Instance);

		await trilha.RegistrarAsync(Registro());

		var enviado = client.Registros.Should().ContainSingle().Subject;
		enviado.Action.Should().Be("mural.publicar");
		enviado.ResourceType.Should().Be("publicacao");
		enviado.ResourceId.Should().Be("abc-123");
		enviado.ActorId.Should().Be(Alguem.Id);
		enviado.ActorName.Should().Be("Ana");
		enviado.ActorType.Should().Be(ActorType.User);
		enviado.OccurredAt.Should().NotBeNull("a trilha registra quando aconteceu, não quando chegou");
	}

	[Fact]
	public async Task SemAtor_NaoRegistra()
	{
		var client = new ClientFalso();
		var trilha = new LogStreamTrilhaDeAuditoria(
			client, new AtorFalso(null), NullLogger<LogStreamTrilhaDeAuditoria>.Instance);

		await trilha.RegistrarAsync(Registro());

		client.Registros.Should().BeEmpty(
			"entrada com ator inventado não prova nada e só sujaria a trilha");
	}

	[Fact]
	public async Task LogStreamForaDoAr_NaoLanca()
	{
		var trilha = new LogStreamTrilhaDeAuditoria(
			new ClientQueFalha(), new AtorFalso(Alguem), NullLogger<LogStreamTrilhaDeAuditoria>.Instance);

		var registrar = async () => await trilha.RegistrarAsync(Registro());

		await registrar.Should().NotThrowAsync(
			"a garantia de não derrubar a ação mora aqui, e não em cada handler");
	}
}
