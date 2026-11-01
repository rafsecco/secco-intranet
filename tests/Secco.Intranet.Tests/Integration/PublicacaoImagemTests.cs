using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Tests.Integration.TestAuthentication;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// A imagem da publicação é servida só a quem pode ler o aviso (no ar e visível) ou a quem
/// escreve no setor dono. Tudo o mais responde 404, igual ao permalink.
/// </summary>
public class PublicacaoImagemTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync()
	{
		factory.ProcessadorDeImagem = null;
		factory.ArquivoStore = null;
		factory.ResolvedorDePermissoes = null;

		return Task.CompletedTask;
	}

	private HttpClient CriarCliente(params string[] roles)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, factory.TenantAlfa.ToString());

		if (roles.Length > 0)
		{
			client.DefaultRequestHeaders.Add(RolesDeTesteMiddleware.Header, string.Join(",", roles));
		}

		return client;
	}

	private async Task<(Guid Id, string Slug)> PublicarAsync(
		Visibilidade visibilidade, DateTimeOffset publicadoEm, bool comImagem = true, string? slug = null)
	{
		using var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		if (slug is null)
		{
			slug = $"img-{Guid.NewGuid():N}"[..20];
			var setor = await escopo.ServiceProvider
				.GetRequiredService<CreateSetorHandler>()
				.HandleAsync(new CreateSetorCommand($"Setor {slug}", slug));
			setor.IsSuccess.Should().BeTrue();
		}

		await using var arquivo = new MemoryStream([1, 2, 3]);

		var publicada = await escopo.ServiceProvider
			.GetRequiredService<PublicarPublicacaoHandler>()
			.HandleAsync(new PublicarPublicacaoCommand(
				slug, "Aviso com imagem", null, TipoPublicacao.Aviso, visibilidade,
				PrioridadePublicacao.Normal, publicadoEm, null, "teste",
				Imagem: comImagem ? new ImagemEnviada(arquivo) : null));

		publicada.IsSuccess.Should().BeTrue();

		return (publicada.Value.Publicacao.Id, slug);
	}

	[Fact]
	public async Task Entregar_ComImagem_200ComCabecalhos()
	{
		var (id, _) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddMinutes(-5));

		var resposta = await CriarCliente().GetAsync($"/publicacoes/{id}/imagem");

		resposta.StatusCode.Should().Be(HttpStatusCode.OK);
		resposta.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
		resposta.Headers.CacheControl!.ToString().Should().Be("no-cache, private");
		resposta.Headers.ETag.Should().NotBeNull();
		resposta.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
		(await resposta.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3);
	}

	[Fact]
	public async Task Entregar_IfNoneMatchIgual_304()
	{
		var (id, _) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddMinutes(-5));
		var client = CriarCliente();
		var primeira = await client.GetAsync($"/publicacoes/{id}/imagem");

		using var pedido = new HttpRequestMessage(HttpMethod.Get, $"/publicacoes/{id}/imagem");
		pedido.Headers.TryAddWithoutValidation("If-None-Match", primeira.Headers.ETag!.ToString());
		var resposta = await client.SendAsync(pedido);

		resposta.StatusCode.Should().Be(HttpStatusCode.NotModified);
	}

	[Fact]
	public async Task Entregar_SemImagem_404()
	{
		var (id, _) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddMinutes(-5), comImagem: false);

		(await CriarCliente().GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Entregar_Inexistente_404() =>
		(await CriarCliente().GetAsync($"/publicacoes/{Guid.NewGuid()}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);

	[Fact]
	public async Task Entregar_SomenteSetor_LeitorDeFora_404()
	{
		var (id, slug) = await PublicarAsync(Visibilidade.Setor, DateTimeOffset.UtcNow.AddMinutes(-5));

		var role = $"dentro-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste().ComPermissao(role, IntranetPermissoes.Setor.Read(slug));

		(await CriarCliente().GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await CriarCliente(role).GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Entregar_Agendada_SoLeitor_404_AutorDoSetor_200()
	{
		var (id, slug) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddDays(1));

		var leitor = $"leitor-{Guid.NewGuid():N}";
		var autor = $"autor-{Guid.NewGuid():N}";
		factory.ResolvedorDePermissoes = new PermissionResolverDeTeste()
			.ComPermissao(leitor, IntranetPermissoes.Setor.Read(slug))
			.ComPermissao(autor, IntranetPermissoes.Setor.Read(slug))
			.ComPermissao(autor, IntranetPermissoes.Setor.Write(slug));

		(await CriarCliente(leitor).GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await CriarCliente(autor).GetAsync($"/publicacoes/{id}/imagem")).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Entregar_ArquivoSumiuDoDisco_404()
	{
		factory.ArquivoStore = new ArquivoStoreFalso { FalhaAoEscrever = new FileNotFoundException("sumiu") };
		var (id, _) = await PublicarAsync(Visibilidade.Empresa, DateTimeOffset.UtcNow.AddMinutes(-5));

		var resposta = await CriarCliente().GetAsync($"/publicacoes/{id}/imagem");

		resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
		resposta.Headers.ETag.Should().BeNull();
	}
}
