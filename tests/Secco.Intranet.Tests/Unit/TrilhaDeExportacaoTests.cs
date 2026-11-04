using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Web.Exportacao;
using Secco.Intranet.Web.Extensibility;
using Secco.SDK.Documents;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// A implementação do contrato que as RCLs usam: o que vai para a trilha (verbo, recurso e um
/// metadata sem conteúdo, com filtros cortados) e a validação do que a RCL informa.
/// </summary>
public class TrilhaDeExportacaoTests
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

	private static ExportacaoParaAuditar Exportacao(
		string recurso = "relatorio-de-teste",
		string formato = "xlsx",
		int linhas = 42,
		IReadOnlyList<FiltroDeExportacao>? filtros = null) =>
		new(recurso, formato, linhas, filtros ?? []);

	private static (TrilhaDeExportacao Trilha, TrilhaObrigatoriaFalsa Falsa) Criar(Result? resultado = null)
	{
		var falsa = new TrilhaObrigatoriaFalsa(resultado ?? Result.Success());

		return (new TrilhaDeExportacao(falsa), falsa);
	}

	private static JsonElement Metadata(TrilhaObrigatoriaFalsa falsa) =>
		JsonDocument.Parse(falsa.Registros.Should().ContainSingle().Subject.Metadata!).RootElement;

	[Fact]
	public async Task Registro_levaVerboRecursoEId()
	{
		var (trilha, falsa) = Criar();

		await trilha.RegistrarAsync(Exportacao(), CancellationToken.None);

		var registro = falsa.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be("exportacao.gerar");
		registro.Recurso.Should().Be("exportacao");
		registro.RecursoId.Should().Be("relatorio-de-teste");
	}

	[Fact]
	public async Task Metadata_temFormatoLinhasEFiltros()
	{
		var (trilha, falsa) = Criar();

		await trilha.RegistrarAsync(
			Exportacao(filtros: [new FiltroDeExportacao("Período", "01/10 a 31/10")]), CancellationToken.None);

		var metadata = Metadata(falsa);
		metadata.GetProperty("formato").GetString().Should().Be("xlsx");
		metadata.GetProperty("linhas").GetInt32().Should().Be(42);
		metadata.GetProperty("filtros").GetProperty("Período").GetString().Should().Be("01/10 a 31/10");
	}

	[Fact]
	public async Task Filtro_longo_eCortadoEm200()
	{
		var (trilha, falsa) = Criar();

		var podeEnviar = await trilha.RegistrarAsync(
			Exportacao(filtros: [new FiltroDeExportacao("Busca", new string('x', 5_000))]), CancellationToken.None);

		podeEnviar.Should().BeTrue("filtro longo é cortado, não derruba a exportação");
		Metadata(falsa).GetProperty("filtros").GetProperty("Busca").GetString().Should().HaveLength(200);
	}

	[Fact]
	public async Task Formato_emMaiusculas_eGravadoEmMinusculas()
	{
		var (trilha, falsa) = Criar();

		await trilha.RegistrarAsync(Exportacao(formato: "PDF"), CancellationToken.None);

		Metadata(falsa).GetProperty("formato").GetString().Should().Be("pdf");
	}

	[Fact]
	public async Task Trilha_aceita_devolveTrue()
	{
		var (trilha, _) = Criar();

		(await trilha.RegistrarAsync(Exportacao(), CancellationToken.None)).Should().BeTrue();
	}

	[Fact]
	public async Task Trilha_recusada_devolveFalse()
	{
		var (trilha, _) = Criar(Result.Failure(IntranetErrors.Auditoria.TrilhaIndisponivel));

		(await trilha.RegistrarAsync(Exportacao(), CancellationToken.None)).Should().BeFalse();
	}

	[Theory]
	[InlineData("")]
	[InlineData("Com Maiuscula")]
	[InlineData("acentuação")]
	[InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
	public async Task Recurso_invalido_lanca(string recurso)
	{
		var (trilha, falsa) = Criar();

		var registrar = async () => await trilha.RegistrarAsync(Exportacao(recurso: recurso), CancellationToken.None);

		await registrar.Should().ThrowAsync<ArgumentException>();
		falsa.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Formato_desconhecido_lanca()
	{
		var (trilha, _) = Criar();

		var registrar = async () => await trilha.RegistrarAsync(Exportacao(formato: "csv"), CancellationToken.None);

		await registrar.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public async Task Linhas_negativas_lanca()
	{
		var (trilha, _) = Criar();

		var registrar = async () => await trilha.RegistrarAsync(Exportacao(linhas: -1), CancellationToken.None);

		await registrar.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public async Task Filtros_nulos_lanca()
	{
		var (trilha, _) = Criar();

		var registrar = async () => await trilha.RegistrarAsync(
			new ExportacaoParaAuditar("relatorio-de-teste", "xlsx", 1, null!), CancellationToken.None);

		await registrar.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public void Secao_invalida_derrubaAValidacao()
	{
		var configuracao = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["Intranet:Exportacao:MaxPdfRows"] = "0" })
			.Build();
		var services = new ServiceCollection().AddSingleton<IConfiguration>(configuracao);
		services.AddIntranetExportacao();
		using var provider = services.BuildServiceProvider();

		var ler = () => provider.GetRequiredService<IOptions<SeccoDocumentsOptions>>().Value;

		ler.Should().Throw<OptionsValidationException>();
	}
}
