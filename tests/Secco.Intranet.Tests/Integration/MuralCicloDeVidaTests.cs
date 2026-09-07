using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A consulta do mural é a definição de "no ar e visível para mim". Cada caso ganha teste
/// próprio porque um erro aqui não aparece como exceção: aparece como comunicado que sumiu,
/// ou como comunicado de outro setor exposto.
/// </summary>
public class MuralCicloDeVidaTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private static readonly DateTimeOffset Agora = DateTimeOffset.UtcNow;

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<(string Slug, Guid PublicacaoId)> SemearAsync(
		Visibilidade visibilidade = Visibilidade.Empresa,
		DateTimeOffset? publicadoEm = null,
		DateTimeOffset? expiraEm = null,
		bool arquivada = false)
	{
		var slug = $"mural-{Guid.NewGuid():N}"[..20];

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		var criarSetor = escopo.ServiceProvider.GetRequiredService<CreateSetorHandler>();
		var criado = await criarSetor.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
		criado.IsSuccess.Should().BeTrue();

		var repositorio = escopo.ServiceProvider.GetRequiredService<IPublicacaoRepository>();
		var publicacao = new Publicacao(
			criado.Value.Id, "Comunicado", "Corpo em **markdown**.", TipoPublicacao.Aviso,
			visibilidade, PrioridadePublicacao.Normal,
			publicadoEm ?? Agora.AddHours(-1), expiraEm, "teste");

		if (arquivada)
		{
			publicacao.Arquivar();
		}

		await repositorio.AddAsync(publicacao);

		return (slug, publicacao.Id);
	}

	/// <summary>
	/// Percorre as páginas até achar a publicação, em vez de assumir que ela cabe na primeira.
	/// Os testes compartilham o tenant e acumulam publicações; presumir a primeira página faria
	/// o caso "sem expiração" — que usa data antiga e por isso fica no fim da ordenação —
	/// quebrar sozinho no dia em que a suíte crescesse.
	/// </summary>
	private async Task<bool> EstaNoMuralAsync(Guid id, params string[] setoresDoLeitor)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var handler = escopo.ServiceProvider.GetRequiredService<ListarMuralHandler>();

		for (var pagina = 1; ; pagina++)
		{
			var resultado = await handler.HandleAsync(new MuralQuery(
				Tipo: null, SetoresDoLeitor: setoresDoLeitor, ExigirVisibilidade: true, Pagina: pagina));

			resultado.IsSuccess.Should().BeTrue();

			if (resultado.Value.Items.Any(publicacao => publicacao.Id == id))
			{
				return true;
			}

			if (!resultado.Value.HasNextPage)
			{
				return false;
			}
		}
	}

	[Fact]
	public async Task NoAr_Aparece()
	{
		var (_, id) = await SemearAsync();

		(await EstaNoMuralAsync(id)).Should().BeTrue();
	}

	[Fact]
	public async Task Agendada_NaoAparece()
	{
		var (_, id) = await SemearAsync(publicadoEm: Agora.AddDays(1));

		(await EstaNoMuralAsync(id)).Should().BeFalse();
	}

	[Fact]
	public async Task Expirada_NaoAparece()
	{
		var (_, id) = await SemearAsync(publicadoEm: Agora.AddDays(-2), expiraEm: Agora.AddDays(-1));

		(await EstaNoMuralAsync(id)).Should().BeFalse();
	}

	[Fact]
	public async Task Arquivada_NaoAparece()
	{
		var (_, id) = await SemearAsync(arquivada: true);

		(await EstaNoMuralAsync(id)).Should().BeFalse();
	}

	[Fact]
	public async Task SemExpiracao_ContinuaAparecendo()
	{
		var (_, id) = await SemearAsync(publicadoEm: Agora.AddYears(-1), expiraEm: null);

		(await EstaNoMuralAsync(id)).Should().BeTrue();
	}

	[Fact]
	public async Task VisibilidadeDeSetor_SemVinculoDoLeitor_NaoAparece()
	{
		var (slug, id) = await SemearAsync(Visibilidade.Setor);

		(await EstaNoMuralAsync(id, "outro-setor")).Should().BeFalse();
		(await EstaNoMuralAsync(id, slug)).Should().BeTrue(
			"com a role do setor, a mesma publicação aparece");
	}

	[Fact]
	public async Task VisibilidadeDeEmpresa_SemVinculoNenhum_Aparece()
	{
		var (_, id) = await SemearAsync(Visibilidade.Empresa);

		(await EstaNoMuralAsync(id)).Should().BeTrue();
	}

	[Fact]
	public async Task FiltroPorTipo_TrazSomenteOTipoPedido()
	{
		var (_, id) = await SemearAsync();

		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);
		var handler = escopo.ServiceProvider.GetRequiredService<ListarMuralHandler>();

		var resultado = await handler.HandleAsync(new MuralQuery(
			Tipo: TipoPublicacao.Evento, SetoresDoLeitor: [], ExigirVisibilidade: true, Pagina: 1));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Items.Should().NotContain(publicacao => publicacao.Id == id,
			"a publicação semeada é um Aviso, e o filtro pediu Evento");
		resultado.Value.Items.Should().OnlyContain(publicacao => publicacao.Tipo == TipoPublicacao.Evento);
	}
}
