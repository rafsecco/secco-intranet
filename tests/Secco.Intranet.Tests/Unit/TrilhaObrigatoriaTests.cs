using AwesomeAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Infrastructure.Auditoria;
using Secco.Intranet.Tests.Support;
using Secco.LogStream.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// A trilha obrigatória da exportação (ADR-0013). O oposto da <see cref="TrilhaDeAuditoriaTests"/>:
/// aqui o que importa é que ela <b>recuse</b> quando não consegue registrar, e que a leniência de
/// DEV dependa do nome do ambiente, não só da URL vazia.
/// </summary>
public class TrilhaObrigatoriaTests
{
	private sealed class AmbienteFalso(string nome) : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = nome;

		public string ApplicationName { get; set; } = "Secco.Intranet.Tests";

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private sealed class AtorFalso(AtorDaAcao? ator) : IAtorAtual
	{
		public AtorDaAcao? Atual() => ator;
	}

	private static readonly AtorDaAcao Alguem = new("018f0000-0000-7000-8000-000000000009", "Ana");

	private static RegistroDeAuditoria Registro() =>
		new(VerbosDeAuditoria.ExportacaoGerar, RecursosDeAuditoria.Exportacao, "relatorio-de-teste", """{"linhas":3}""");

	private static LogStreamTrilhaObrigatoria LogStream(
		ILogStreamClient client, string ambiente = "Production", AtorDaAcao? ator = null) =>
		new(client, new AtorFalso(ator ?? Alguem), new AmbienteFalso(ambiente), NullLogger<LogStreamTrilhaObrigatoria>.Instance);

	[Theory]
	[InlineData("Development")]
	[InlineData("Testing")]
	[InlineData("Staging")]
	[InlineData("Production")]
	public void Escolha_comLogStream_usaLogStream_emQualquerAmbiente(string ambiente)
	{
		EscolhaDaTrilhaObrigatoria.Para("https://logstream", new AmbienteFalso(ambiente))
			.Should().Be(ModoDaTrilhaObrigatoria.LogStream);
	}

	[Theory]
	[InlineData("Development", "")]
	[InlineData("Development", null)]
	[InlineData("Testing", "")]
	[InlineData("Testing", "  ")]
	public void Escolha_semLogStream_emDev_aceita(string ambiente, string? url)
	{
		EscolhaDaTrilhaObrigatoria.Para(url, new AmbienteFalso(ambiente))
			.Should().Be(ModoDaTrilhaObrigatoria.Desenvolvimento);
	}

	[Theory]
	[InlineData("Staging")]
	[InlineData("Production")]
	[InlineData("Homologacao")]
	public void Escolha_semLogStream_foraDeDev_recusa(string ambiente)
	{
		EscolhaDaTrilhaObrigatoria.Para("", new AmbienteFalso(ambiente))
			.Should().Be(ModoDaTrilhaObrigatoria.Indisponivel,
				"um HML com a URL esquecida não pode exportar sem trilha");
	}

	[Fact]
	public async Task LogStream_aceito_devolveSucesso_eEnviaOVerbo()
	{
		var client = LogStreamClientDeTeste.QueAceita();

		var resultado = await LogStream(client).RegistrarAsync(Registro());

		resultado.IsSuccess.Should().BeTrue();
		var enviado = client.Registros.Should().ContainSingle().Subject;
		enviado.Action.Should().Be("exportacao.gerar");
		enviado.ResourceType.Should().Be("exportacao");
		enviado.ResourceId.Should().Be("relatorio-de-teste");
		enviado.ActorId.Should().Be(Alguem.Id);
	}

	[Fact]
	public async Task LogStream_falhaDeRede_recusa_mesmoEmDevelopment()
	{
		var client = LogStreamClientDeTeste.QueLanca(new HttpRequestException("LogStream fora do ar."));

		var resultado = await LogStream(client, "Development").RegistrarAsync(Registro());

		resultado.IsFailure.Should().BeTrue("com URL configurada, a leniência de DEV não vale");
		resultado.Error.Should().Be(IntranetErrors.Auditoria.TrilhaIndisponivel);
	}

	[Fact]
	public async Task LogStream_recusaDoServico_recusa()
	{
		var client = LogStreamClientDeTeste.QueLanca(
			new ApiException("recusado", 500, null, new Dictionary<string, IEnumerable<string>>(), null));

		var resultado = await LogStream(client).RegistrarAsync(Registro());

		resultado.Error.Should().Be(IntranetErrors.Auditoria.TrilhaIndisponivel);
	}

	[Fact]
	public async Task LogStream_timeout_recusa()
	{
		var client = LogStreamClientDeTeste.QueLanca(new TaskCanceledException("Timeout do HttpClient."));

		var resultado = await LogStream(client).RegistrarAsync(Registro());

		resultado.Error.Should().Be(IntranetErrors.Auditoria.TrilhaIndisponivel);
	}

	[Fact]
	public async Task LogStream_cancelamentoDoChamador_propaga()
	{
		var client = new LogStreamClientDeTeste((_, token) =>
		{
			token.ThrowIfCancellationRequested();

			return Task.FromResult(new AuditEntryDto());
		});
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var registrar = async () => await LogStream(client).RegistrarAsync(Registro(), cts.Token);

		await registrar.Should().ThrowAsync<OperationCanceledException>(
			"o navegador abandonou a requisição; não é falha de infraestrutura");
	}

	[Fact]
	public async Task LogStream_semAtor_emTesting_aceita()
	{
		var client = LogStreamClientDeTeste.QueAceita();
		var trilha = new LogStreamTrilhaObrigatoria(
			client, new AtorFalso(null), new AmbienteFalso("Testing"), NullLogger<LogStreamTrilhaObrigatoria>.Instance);

		var resultado = await trilha.RegistrarAsync(Registro());

		resultado.IsSuccess.Should().BeTrue();
		client.Registros.Should().BeEmpty("registro com ator inventado não prova nada");
	}

	[Fact]
	public async Task LogStream_semAtor_emProduction_recusa()
	{
		var trilha = new LogStreamTrilhaObrigatoria(
			LogStreamClientDeTeste.QueAceita(), new AtorFalso(null), new AmbienteFalso("Production"),
			NullLogger<LogStreamTrilhaObrigatoria>.Instance);

		var resultado = await trilha.RegistrarAsync(Registro());

		resultado.Error.Should().Be(IntranetErrors.Auditoria.TrilhaIndisponivel);
	}

	[Fact]
	public async Task Desenvolvimento_aceita()
	{
		var resultado = await new TrilhaObrigatoriaDeDesenvolvimento(
			NullLogger<TrilhaObrigatoriaDeDesenvolvimento>.Instance).RegistrarAsync(Registro());

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task Indisponivel_recusa()
	{
		var resultado = await new TrilhaObrigatoriaIndisponivel(
			NullLogger<TrilhaObrigatoriaIndisponivel>.Instance).RegistrarAsync(Registro());

		resultado.Error.Should().Be(IntranetErrors.Auditoria.TrilhaIndisponivel);
	}
}
