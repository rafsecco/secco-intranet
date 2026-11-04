using Secco.LogStream.Client;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// Dublê de <see cref="ILogStreamClient"/> em que só a criação de registro de auditoria importa: o
/// comportamento dela vem de <paramref name="criar"/>, e os registros recebidos ficam em
/// <see cref="Registros"/>. Os demais membros lançam <see cref="NotImplementedException"/>.
/// </summary>
/// <param name="criar">O que <c>CreateAuditEntryAsync</c> faz; nulo = aceita.</param>
public sealed class LogStreamClientDeTeste(
	Func<CreateAuditEntryRequest, CancellationToken, Task<AuditEntryDto>>? criar = null) : ILogStreamClient
{
	/// <summary>Registros recebidos, na ordem.</summary>
	public List<CreateAuditEntryRequest> Registros { get; } = [];

	/// <summary>Client que aceita todo registro.</summary>
	public static LogStreamClientDeTeste QueAceita() => new();

	/// <summary>Client que lança <paramref name="excecao"/> ao registrar.</summary>
	/// <param name="excecao">Exceção lançada.</param>
	public static LogStreamClientDeTeste QueLanca(Exception excecao) => new((_, _) => throw excecao);

	public Task<AuditEntryDto> CreateAuditEntryAsync(CreateAuditEntryRequest body, CancellationToken cancellationToken)
	{
		Registros.Add(body);

		return criar is null ? Task.FromResult(new AuditEntryDto()) : criar(body, cancellationToken);
	}

	public Task<AuditEntryDto> CreateAuditEntryAsync(CreateAuditEntryRequest body) =>
		CreateAuditEntryAsync(body, CancellationToken.None);

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
	public Task<FileResponse> ExportAuditEntriesAsync(string format, DateTimeOffset? from, DateTimeOffset? to, string actorId, string action, string resourceType, string resourceId, Guid? correlationId, string timeZone) => throw new NotImplementedException();
	public Task<FileResponse> ExportAuditEntriesAsync(string format, DateTimeOffset? from, DateTimeOffset? to, string actorId, string action, string resourceType, string resourceId, Guid? correlationId, string timeZone, CancellationToken cancellationToken) => throw new NotImplementedException();
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
