using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>Grade do dia, reserva, detalhe e "Minhas reservas" de ponta a ponta, com o formulário real.</summary>
public partial class ReservaDeSalaTelasTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

	private static readonly Guid Bruno = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

	private static readonly Guid Carla = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

	private static int _deslocamento = 20;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		factory.UsuariosDoDiretorio = new UsuariosParaDiretorioFalso()
			.Com(Ana, "ana.teste@example.com", "Ana Teste")
			.Com(Bruno, "bruno.teste@example.com", "Bruno Teste")
			.Com(Carla, "carla.teste@example.com", "Carla Teste");
	}

	public Task DisposeAsync() => Task.CompletedTask;

	[GeneratedRegex("\\sstyle\\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex AtributoStyle();

	[GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase)]
	private static partial Regex ScriptInline();

	[GeneratedRegex("\\son[a-z]+\\s*=", RegexOptions.IgnoreCase)]
	private static partial Regex ManipuladorInline();

	private static string DiaUnico() =>
		DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Increment(ref _deslocamento)).ToString("yyyy-MM-dd");

	private static string NomeUnico() => $"Sala {Guid.NewGuid():N}"[..20];

	private HttpClient CriarCliente(Guid? usuario = null, bool admin = false, string tema = "Vertical", Guid? tenant = null)
	{
		var client = factory
			.WithWebHostBuilder(builder => builder.UseSetting("Intranet:Theme:Nome", tema))
			.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, (tenant ?? factory.TenantAlfa).ToString());
		client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, admin ? "intranet-admin" : "diretorio-user");

		if (usuario is not null)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.HeaderUsuario, usuario.Value.ToString());
		}

		return client;
	}

	private async Task<(Guid Id, string Nome)> CriarSalaAsync(bool ativa = true)
	{
		var nome = NomeUnico();
		var admin = CriarCliente(Ana, admin: true);
		var token = await AuxiliaresDeHttp.TokenAsync(admin, "/salas/gerenciar/nova");

		(await admin.PostAsync("/salas/gerenciar/nova", AuxiliaresDeHttp.Form(
			("__RequestVerificationToken", token),
			("Nome", nome),
			("Capacidade", "8"),
			("Ordem", "1"),
			("Ativa", ativa ? "true" : "false")))).EnsureSuccessStatusCode();

		await using var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var salas = await escopo.ServiceProvider.GetRequiredService<ListarSalasHandler>().HandleAsync(somenteAtivas: false);

		return (salas.Single(sala => sala.Nome == nome).Id, nome);
	}

	private static async Task<HttpResponseMessage> ReservarAsync(
		HttpClient client, Guid sala, string dia, string titulo, string inicio = "10:00", string fim = "11:00", params Guid[] convidados)
	{
		var token = await AuxiliaresDeHttp.TokenAsync(client, "/salas/reservar");
		var campos = new List<(string, string)>
		{
			("__RequestVerificationToken", token),
			("SalaId", sala.ToString()),
			("Data", dia),
			("Inicio", inicio),
			("Fim", fim),
			("Titulo", titulo),
			("Descricao", "Pauta de teste"),
		};
		campos.AddRange(convidados.Select(convidado => ("Convidados", convidado.ToString())));

		return await client.PostAsync("/salas/reservar", AuxiliaresDeHttp.Form([.. campos]));
	}

	private static async Task<string> TextoAsync(HttpResponseMessage resposta) =>
		AuxiliaresDeHttp.Decodificar(await resposta.Content.ReadAsStringAsync());

	private static string IdDaReservaNoHtml(string html)
	{
		var achado = Regex.Match(html, "/salas/reservas/([0-9a-f-]{36})");
		achado.Success.Should().BeTrue("a grade precisa linkar a reserva");

		return achado.Groups[1].Value;
	}

	[Fact]
	public async Task Grade_MostraSalasAtivasEAReserva()
	{
		var ativa = await CriarSalaAsync();
		var inativa = await CriarSalaAsync(ativa: false);
		var dia = DiaUnico();
		var ana = CriarCliente(Ana);
		(await ReservarAsync(ana, ativa.Id, dia, "Alinhamento semanal")).EnsureSuccessStatusCode();

		var html = AuxiliaresDeHttp.Decodificar(await ana.GetStringAsync($"/salas?data={dia}"));

		html.Should().Contain(ativa.Nome).And.Contain("Alinhamento semanal").And.Contain("Ana Teste");
		html.Should().Contain("10:00");
		html.Should().NotContain(inativa.Nome);
		html.Should().Contain($"href=\"/salas/reservar?sala={ativa.Id}&data={dia}&inicio=");
	}

	[Fact]
	public async Task Grade_SemSalas_EstadoVazio()
	{
		var visitante = CriarCliente(Bruno, tenant: factory.TenantBeta);
		var admin = CriarCliente(Ana, admin: true, tenant: factory.TenantBeta);

		var htmlVisitante = AuxiliaresDeHttp.Decodificar(await visitante.GetStringAsync("/salas"));
		var htmlAdmin = AuxiliaresDeHttp.Decodificar(await admin.GetStringAsync("/salas"));

		htmlVisitante.Should().Contain("Nenhuma sala cadastrada");
		htmlVisitante.Should().NotContain("/salas/gerenciar");
		htmlAdmin.Should().Contain("Nenhuma sala cadastrada").And.Contain("/salas/gerenciar/nova");
	}

	[Fact]
	public async Task Grade_DataInvalidaNaUrl_CaiEmHoje()
	{
		await CriarSalaAsync();
		await using var escopo = factory.Services.CreateAsyncScope();
		var hoje = escopo.ServiceProvider.GetRequiredService<RelogioDeSalas>().Hoje().ToString("yyyy-MM-dd");

		var resposta = await CriarCliente(Ana).GetAsync("/salas?data=isso-nao-e-data");
		var html = await TextoAsync(resposta);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		html.Should().Contain($"value=\"{hoje}\"");
	}

	[Fact]
	public async Task Reservar_Valida_ApareceNaGrade()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		var ana = CriarCliente(Ana);

		var resposta = await ReservarAsync(ana, sala.Id, dia, "Planejamento do trimestre", convidados: Bruno);
		var html = await TextoAsync(resposta);

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/salas");
		resposta.RequestMessage.RequestUri.Query.Should().Contain(dia);
		html.Should().Contain("Reserva criada.").And.Contain("Planejamento do trimestre");
	}

	[Fact]
	public async Task Reservar_Conflito_MostraQuemEstaComASala_ENaoGrava()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		(await ReservarAsync(CriarCliente(Ana), sala.Id, dia, "Reserva da Ana", "14:00", "15:00")).EnsureSuccessStatusCode();
		var bruno = CriarCliente(Bruno);

		var resposta = await ReservarAsync(bruno, sala.Id, dia, "Reserva do Bruno", "14:30", "15:30");
		var html = await TextoAsync(resposta);

		html.Should().Contain("Sala ocupada").And.Contain("Ana Teste");
		html.Should().Contain("Reserva do Bruno", "o formulário volta com o que foi digitado");
		var grade = AuxiliaresDeHttp.Decodificar(await bruno.GetStringAsync($"/salas?data={dia}"));
		grade.Should().Contain("Reserva da Ana").And.NotContain("Reserva do Bruno");
	}

	[Fact]
	public async Task Reservar_SemAntiforgery_400()
	{
		var sala = await CriarSalaAsync();

		var resposta = await CriarCliente(Ana).PostAsync("/salas/reservar", AuxiliaresDeHttp.Form(
			("SalaId", sala.Id.ToString()), ("Data", DiaUnico()), ("Inicio", "10:00"), ("Fim", "11:00"), ("Titulo", "Sem token")));

		resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Detalhe_Dono_VeAlterarECancelar_Outro_NaoVe()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		var ana = CriarCliente(Ana);
		var grade = AuxiliaresDeHttp.Decodificar(await (await ReservarAsync(ana, sala.Id, dia, "Reuniao de detalhe", convidados: Carla)).Content.ReadAsStringAsync());
		var id = IdDaReservaNoHtml(grade);

		var doDono = AuxiliaresDeHttp.Decodificar(await ana.GetStringAsync($"/salas/reservas/{id}"));
		var doOutro = AuxiliaresDeHttp.Decodificar(await CriarCliente(Bruno).GetStringAsync($"/salas/reservas/{id}"));

		doDono.Should().Contain("Reuniao de detalhe").And.Contain("Carla Teste");
		doDono.Should().Contain($"/salas/reservas/{id}/alterar").And.Contain($"/salas/reservas/{id}/cancelar");
		doOutro.Should().Contain("Reuniao de detalhe").And.Contain("Ana Teste");
		doOutro.Should().NotContain("/alterar").And.NotContain("/cancelar");
	}

	[Fact]
	public async Task Alterar_DeOutraPessoa_404()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		var grade = await TextoAsync(await ReservarAsync(CriarCliente(Ana), sala.Id, dia, "Reserva alheia"));
		var id = IdDaReservaNoHtml(grade);
		var bruno = CriarCliente(Bruno);

		(await bruno.GetAsync($"/salas/reservas/{id}/alterar")).StatusCode.Should().Be(HttpStatusCode.NotFound);

		var token = await AuxiliaresDeHttp.TokenAsync(bruno, "/salas/reservar");
		var post = await bruno.PostAsync($"/salas/reservas/{id}/alterar", AuxiliaresDeHttp.Form(
			("__RequestVerificationToken", token), ("SalaId", sala.Id.ToString()), ("Data", dia),
			("Inicio", "10:00"), ("Fim", "11:00"), ("Titulo", "Sequestrada")));

		post.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Alterar_Dono_MudaOTitulo()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		var ana = CriarCliente(Ana);
		var id = IdDaReservaNoHtml(await TextoAsync(await ReservarAsync(ana, sala.Id, dia, "Titulo antigo")));
		var token = await AuxiliaresDeHttp.TokenAsync(ana, $"/salas/reservas/{id}/alterar");

		var resposta = await ana.PostAsync($"/salas/reservas/{id}/alterar", AuxiliaresDeHttp.Form(
			("__RequestVerificationToken", token), ("SalaId", sala.Id.ToString()), ("Data", dia),
			("Inicio", "10:00"), ("Fim", "11:00"), ("Titulo", "Titulo novo")));
		var html = await TextoAsync(resposta);

		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/salas");
		html.Should().Contain("Titulo novo").And.NotContain("Titulo antigo");
	}

	[Fact]
	public async Task Cancelar_DeOutraPessoa_404_Admin_Cancela()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		var id = IdDaReservaNoHtml(await TextoAsync(await ReservarAsync(CriarCliente(Ana), sala.Id, dia, "Para cancelar")));
		var bruno = CriarCliente(Bruno);
		var tokenBruno = await AuxiliaresDeHttp.TokenAsync(bruno, "/salas/reservar");

		var negado = await bruno.PostAsync($"/salas/reservas/{id}/cancelar", AuxiliaresDeHttp.Form(("__RequestVerificationToken", tokenBruno)));

		negado.StatusCode.Should().Be(HttpStatusCode.NotFound);

		var admin = CriarCliente(Carla, admin: true);
		var tokenAdmin = await AuxiliaresDeHttp.TokenAsync(admin, "/salas/reservar");
		var feito = await admin.PostAsync($"/salas/reservas/{id}/cancelar", AuxiliaresDeHttp.Form(("__RequestVerificationToken", tokenAdmin)));

		(await TextoAsync(feito)).Should().NotContain("Para cancelar", "a reserva saiu da grade");
		(await admin.GetAsync($"/salas/reservas/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Cancelar_Dono_SomeDaGrade()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		var ana = CriarCliente(Ana);
		var id = IdDaReservaNoHtml(await TextoAsync(await ReservarAsync(ana, sala.Id, dia, "Vai sumir")));
		var token = await AuxiliaresDeHttp.TokenAsync(ana, "/salas/reservar");

		var resposta = await ana.PostAsync($"/salas/reservas/{id}/cancelar", AuxiliaresDeHttp.Form(("__RequestVerificationToken", token)));
		var html = await TextoAsync(resposta);

		resposta.RequestMessage!.RequestUri!.AbsolutePath.Should().Be("/salas");
		html.Should().Contain("Reserva cancelada.").And.NotContain("Vai sumir");
	}

	[Fact]
	public async Task Minhas_TrazAsQueFizEAsQueFuiConvidado()
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		(await ReservarAsync(CriarCliente(Ana), sala.Id, dia, "Feita pela Ana", "08:00", "09:00")).EnsureSuccessStatusCode();
		(await ReservarAsync(CriarCliente(Bruno), sala.Id, dia, "Do Bruno com Ana", "09:00", "10:00", Ana)).EnsureSuccessStatusCode();
		(await ReservarAsync(CriarCliente(Bruno), sala.Id, dia, "Do Bruno sozinho", "10:00", "11:00")).EnsureSuccessStatusCode();

		var html = AuxiliaresDeHttp.Decodificar(await CriarCliente(Ana).GetStringAsync("/salas/minhas"));

		html.Should().Contain("Feita pela Ana").And.Contain("Do Bruno com Ana").And.NotContain("Do Bruno sozinho");
	}

	[Theory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Telas_SemEstiloNemScriptEmbutido(string tema)
	{
		var sala = await CriarSalaAsync();
		var dia = DiaUnico();
		var ana = CriarCliente(Ana, tema: tema);
		var id = IdDaReservaNoHtml(await TextoAsync(await ReservarAsync(ana, sala.Id, dia, "Tela segura", convidados: Bruno)));

		foreach (var caminho in new[] { $"/salas?data={dia}", $"/salas/reservar?sala={sala.Id}&data={dia}&inicio=09:00", $"/salas/reservas/{id}", "/salas/minhas" })
		{
			var resposta = await ana.GetAsync(caminho);
			resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"{caminho} no tema {tema} precisa abrir");
			var html = await resposta.Content.ReadAsStringAsync();

			AtributoStyle().Matches(html).Should().BeEmpty($"{caminho} no tema {tema} não pode ter atributo style");
			ScriptInline().Matches(html).Should().BeEmpty($"{caminho} no tema {tema} não pode ter script sem src");
			ManipuladorInline().Matches(html).Should().BeEmpty($"{caminho} no tema {tema} não pode ter manipulador inline");
		}

		(await ana.GetStringAsync($"/salas/reservar?sala={sala.Id}&data={dia}")).Should().Contain("/js/salas.js");
	}
}
