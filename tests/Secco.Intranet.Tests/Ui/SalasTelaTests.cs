using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Playwright;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.SharedKernel.Constants;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Secco.Intranet.Tests.Ui;

/// <summary>
/// A reserva de sala num navegador de verdade, nos dois temas: a grade do dia (todas as salas lado a lado no
/// desktop, uma por vez com abas no celular), o clique no espaço livre, a reserva pelo formulário (com e
/// sem JavaScript), a busca de convidados e a CSP. Usa as duas salas e a reserva da <see cref="TelaFixture"/>.
/// </summary>
public class SalasTelaTests(TelaFixture f) : IClassFixture<TelaFixture>
{
	private static string Texto(DateOnly dia) => dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private static async Task<bool> TransbordaAsync(IPage pagina) =>
		await pagina.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth");

	private string DiaDaReserva => Texto(f.DiaDaReservaFicticia);

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Desktop_TodasAsSalasLadoALado_SemRolagemHorizontal(string tema)
	{
		var pagina = await f.AbrirSalasAsync(tema, $"/salas?data={DiaDaReserva}&sala={f.SalaAId}", largura: 1280);

		var salaA = pagina.Locator($"th.sc-salas__sala:has-text('{f.NomeDaSalaA}')");
		var salaB = pagina.Locator($"th.sc-salas__sala:has-text('{f.NomeDaSalaB}')");
		await Expect(salaA).ToBeVisibleAsync();
		await Expect(salaB).ToBeVisibleAsync();
		var a = await salaA.BoundingBoxAsync();
		var b = await salaB.BoundingBoxAsync();
		a!.Y.Should().BeApproximately(b!.Y, 1, "as salas aparecem lado a lado, na mesma linha");
		await Expect(pagina.Locator(".sc-salas__reserva-link", new() { HasTextString = f.TituloDaReservaFicticia })).ToBeVisibleAsync();
		(await TransbordaAsync(pagina)).Should().BeFalse("a grade não pode rolar a página na horizontal");
		f.ErrosDeScript(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Celular_UmaSalaPorVez_AbaTrocaASala_SemRolagemHorizontal(string tema)
	{
		var pagina = await f.AbrirSalasAsync(tema, $"/salas?data={DiaDaReserva}&sala={f.SalaAId}", largura: 375, altura: 700);

		var salaA = pagina.Locator($"th.sc-salas__sala:has-text('{f.NomeDaSalaA}')");
		var salaB = pagina.Locator($"th.sc-salas__sala:has-text('{f.NomeDaSalaB}')");
		await Expect(salaA).ToBeVisibleAsync();
		await Expect(salaB).ToBeHiddenAsync();
		(await TransbordaAsync(pagina)).Should().BeFalse("no celular a grade não rola a página na horizontal");

		await pagina.Locator(".sc-salas__aba", new() { HasTextString = f.NomeDaSalaB }).ClickAsync();

		await Expect(pagina).ToHaveURLAsync(new Regex($"sala={f.SalaBId}", RegexOptions.IgnoreCase));
		await Expect(salaB).ToBeVisibleAsync();
		await Expect(salaA).ToBeHiddenAsync();
		(await TransbordaAsync(pagina)).Should().BeFalse();
		f.ErrosDeScript(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task CliqueNoEspacoLivre_AbreOFormularioPreenchido(string tema)
	{
		var pagina = await f.AbrirSalasAsync(tema, $"/salas?data={DiaDaReserva}&sala={f.SalaBId}");

		await pagina.Locator($"a.sc-salas__livre-link[href*='sala={f.SalaBId}'][href$='inicio=14%3A00']").ClickAsync();

		await Expect(pagina).ToHaveURLAsync(new Regex("inicio=14(:|%3A)00$", RegexOptions.IgnoreCase));
		await Expect(pagina.Locator("#reserva-sala")).ToHaveValueAsync(f.SalaBId.ToString());
		await Expect(pagina.Locator("#reserva-data")).ToHaveValueAsync(f.DiaDaReservaFicticia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
		await Expect(pagina.Locator("#reserva-inicio")).ToHaveValueAsync("14:00");
		await Expect(pagina.Locator("#reserva-fim")).ToHaveValueAsync("15:00");
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Reservar_ApareceComoBlocoNaGrade(string tema)
	{
		var titulo = $"Reunião {Guid.NewGuid():N}"[..20];
		var dia = Texto(f.DiaDaReservaFicticia.AddDays(tema == "Vertical" ? 11 : 12));
		var pagina = await f.AbrirSalasAsync(tema, $"/salas/reservar?sala={f.SalaAId}&data={dia}&inicio=14:00");

		await pagina.GetByLabel("Título", new() { Exact = true }).FillAsync(titulo);
		await pagina.GetByRole(AriaRole.Button, new() { Name = "Reservar", Exact = true }).ClickAsync();

		await Expect(pagina).ToHaveURLAsync(new Regex($"/salas\\?data={dia}", RegexOptions.IgnoreCase));
		var bloco = pagina.Locator("td.sc-salas__reserva", new() { HasTextString = titulo });
		await Expect(bloco).ToBeVisibleAsync();
		await Expect(bloco).ToHaveAttributeAsync("rowspan", "4");
		f.ErrosDeScript(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Capacidade_AcompanhaASalaEAvisaQuandoLota(string tema)
	{
		var pagina = await f.AbrirSalasAsync(tema, $"/salas/reservar?sala={f.SalaBId}&data={DiaDaReserva}");
		var capacidade = pagina.Locator("[data-salas-capacidade]");
		var aviso = pagina.Locator("[data-salas-lotacao]");
		await Expect(capacidade).ToHaveTextAsync("Capacidade: 4 pessoas.");
		await Expect(aviso).ToBeHiddenAsync();

		var caixas = pagina.Locator("input[name='Convidados']");
		var convidados = await caixas.CountAsync();
		convidados.Should().BeGreaterThanOrEqualTo(4, "a Sala B tem 4 lugares e quem reserva ocupa um");
		for (var i = 0; i < convidados; i++)
		{
			await caixas.Nth(i).CheckAsync();
		}

		await Expect(aviso).ToBeVisibleAsync();
		await Expect(aviso).ToContainTextAsync($"{convidados + 1} participantes");

		await pagina.Locator("#reserva-sala").SelectOptionAsync(f.SalaAId.ToString());

		await Expect(capacidade).ToHaveTextAsync("Capacidade: 8 pessoas.");
		if (convidados + 1 <= 8)
		{
			await Expect(aviso).ToBeHiddenAsync();
		}

		f.ErrosDeScript(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task BuscaDeConvidados_FiltraALista(string tema)
	{
		var pagina = await f.AbrirSalasAsync(tema, $"/salas/reservar?sala={f.SalaAId}&data={DiaDaReserva}");
		var caio = pagina.Locator(".sc-salas__convidado", new() { HasTextString = $"Caio {f.SufixoDoOrganograma}" });
		var bruno = pagina.Locator(".sc-salas__convidado", new() { HasTextString = $"Bruno {f.SufixoDoOrganograma}" });
		await Expect(caio).ToBeVisibleAsync();
		await Expect(bruno).ToBeVisibleAsync();

		await pagina.GetByLabel("Buscar convidado").FillAsync($"caio {f.SufixoDoOrganograma}");

		await Expect(caio).ToBeVisibleAsync();
		await Expect(bruno).ToBeHiddenAsync();

		await pagina.GetByLabel("Buscar convidado").FillAsync(string.Empty);

		await Expect(bruno).ToBeVisibleAsync();
		f.ErrosDeScript(pagina).Should().BeEmpty();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task SemJavaScript_FormularioGrava(string tema)
	{
		var titulo = $"Sem script {Guid.NewGuid():N}"[..20];
		var dia = Texto(f.DiaDaReservaFicticia.AddDays(tema == "Vertical" ? 13 : 14));
		var pagina = await f.AbrirSalasAsync(tema, $"/salas/reservar?sala={f.SalaBId}&data={dia}&inicio=09:00", javaScript: false);

		await Expect(pagina.GetByLabel("Buscar convidado")).ToHaveCountAsync(0);
		await Expect(pagina.Locator(".sc-salas__convidado", new() { HasTextString = $"Ana {f.SufixoDoOrganograma}" })).ToBeVisibleAsync();
		await pagina.GetByLabel("Título", new() { Exact = true }).FillAsync(titulo);
		await pagina.GetByRole(AriaRole.Button, new() { Name = "Reservar", Exact = true }).ClickAsync();

		await Expect(pagina).ToHaveURLAsync(new Regex($"/salas\\?data={dia}", RegexOptions.IgnoreCase));
		await Expect(pagina.Locator("td.sc-salas__reserva", new() { HasTextString = titulo })).ToBeVisibleAsync();
	}

	[UiTheory]
	[InlineData("Vertical")]
	[InlineData("Horizontal")]
	public async Task Telas_SemViolacaoDeCsp(string tema)
	{
		var pagina = await f.AbrirSalasAsync(tema, $"/salas?data={DiaDaReserva}&sala={f.SalaAId}");
		var reserva = pagina.Locator(".sc-salas__reserva-link", new() { HasTextString = f.TituloDaReservaFicticia });
		await Expect(reserva).ToBeVisibleAsync();

		await reserva.ClickAsync();
		await Expect(pagina).ToHaveURLAsync(new Regex("/salas/reservas/[0-9a-f-]{36}$", RegexOptions.IgnoreCase));
		await pagina.GotoAsync($"/salas/reservar?sala={f.SalaAId}&data={DiaDaReserva}");
		await pagina.GotoAsync("/salas/minhas");

		// A gestão das salas é só do intranet-admin.
		await pagina.Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
		{
			[SeccoHeaders.TenantId] = f.Tenant.ToString(),
			[RolesDeTesteMiddleware.Header] = "intranet-admin",
			[RolesDeTesteMiddleware.HeaderUsuario] = f.UsuarioDeSalas.ToString(),
		});
		await pagina.GotoAsync("/salas/gerenciar");
		await Expect(pagina.GetByText(f.NomeDaSalaA)).ToBeVisibleAsync();
		await pagina.GotoAsync("/salas/gerenciar/nova");
		await Expect(pagina.GetByLabel("Nome", new() { Exact = true })).ToBeVisibleAsync();

		f.ViolacoesDeCsp(pagina).Should().BeEmpty("as telas de salas não escrevem script nem style inline");
		f.ErrosDeScript(pagina).Should().BeEmpty();
	}
}
