using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Tests.Integration.Exportacao;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Secco.SharedKernel.Results;
using Xunit;
using static Secco.Intranet.Tests.Support.AuxiliaresDeHttp;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Exportação por um controller de "RCL externa" (<see cref="ExportacaoDeTesteController"/>) no host
/// real, em Testing e sem LogStream (ADR-0013): o caminho feliz, a recusa quando a trilha falha, a
/// ordem trilha → envio e o antiforgery. Cada teste usa um usuário novo, para não gastar o limite
/// por pessoa de outro.
/// </summary>
public class ExportacaoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private sealed class TrilhaObrigatoriaFalsa(Result resultado) : ITrilhaObrigatoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task<Result> RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.FromResult(resultado);
		}
	}

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.TrilhaObrigatoria = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente()
	{
		var client = factory.CreateClient(new() { AllowAutoRedirect = false });
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, Guid.NewGuid().ToString());

		return client;
	}

	private static async Task<HttpResponseMessage> ExportarAsync(HttpClient client, string formato, bool comToken = true)
	{
		var token = await client.GetStringAsync(ExportacaoDeTesteController.Caminho);
		var campos = new List<(string, string)> { ("formato", formato), ("filtro", "outubro") };

		if (comToken)
		{
			campos.Add(("__RequestVerificationToken", token));
		}

		return await client.PostAsync(ExportacaoDeTesteController.Caminho, Form([.. campos]));
	}

	[Fact]
	public async Task Testing_semLogStream_exporta()
	{
		var resposta = await ExportarAsync(CriarCliente(), "xlsx");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
		resposta.Content.Headers.ContentType!.MediaType
			.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
		(await resposta.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
	}

	[Fact]
	public async Task Pdf_exporta()
	{
		var resposta = await ExportarAsync(CriarCliente(), "pdf");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
	}

	[Fact]
	public async Task TrilhaQueFalha_naoEnvia()
	{
		factory.TrilhaObrigatoria = new TrilhaObrigatoriaFalsa(Result.Failure(IntranetErrors.Auditoria.TrilhaIndisponivel));

		try
		{
			var client = CriarCliente();
			var resposta = await ExportarAsync(client, "xlsx");

			resposta.StatusCode.Should().Be(HttpStatusCode.Redirect);
			resposta.Headers.Location!.OriginalString.Should().Be(ExportacaoDeTesteController.Caminho);
			resposta.Content.Headers.ContentDisposition.Should().BeNull("sem trilha, o arquivo não sai");
			(await client.GetStringAsync(ExportacaoDeTesteController.Caminho + "/mensagem"))
				.Should().Be(ExportacaoDeTesteController.MensagemDeRecusa,
					"o TempData sobrevive ao redirect devolvido como IResult, como o guia ensina");
		}
		finally
		{
			factory.TrilhaObrigatoria = null;
		}
	}

	[Fact]
	public async Task TrilhaRegistra_antesDoEnvio()
	{
		var falsa = new TrilhaObrigatoriaFalsa(Result.Success());
		factory.TrilhaObrigatoria = falsa;

		try
		{
			var resposta = await ExportarAsync(CriarCliente(), "xlsx");

			resposta.StatusCode.Should().Be(HttpStatusCode.OK);
			var registro = falsa.Registros.Should().ContainSingle().Subject;
			registro.Verbo.Should().Be(VerbosDeAuditoria.ExportacaoGerar);
			registro.RecursoId.Should().Be(ExportacaoDeTesteController.Recurso);
			var metadata = JsonDocument.Parse(registro.Metadata!).RootElement;
			metadata.GetProperty("linhas").GetInt32().Should().Be(3);
			metadata.GetProperty("filtros").GetProperty("Filtro").GetString().Should().Be("outubro");
		}
		finally
		{
			factory.TrilhaObrigatoria = null;
		}
	}

	[Fact]
	public async Task SemAntiforgery_recusa()
	{
		var falsa = new TrilhaObrigatoriaFalsa(Result.Success());
		factory.TrilhaObrigatoria = falsa;

		try
		{
			var resposta = await ExportarAsync(CriarCliente(), "xlsx", comToken: false);

			resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
			falsa.Registros.Should().BeEmpty();
		}
		finally
		{
			factory.TrilhaObrigatoria = null;
		}
	}
}
