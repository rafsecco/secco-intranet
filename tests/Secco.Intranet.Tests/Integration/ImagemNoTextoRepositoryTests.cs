using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// Persistência das imagens no texto contra o banco de verdade. As gravações passam pelo
/// <c>SaveChangesAsync</c> do repositório de publicações, o que prova o contexto compartilhado.
/// </summary>
public class ImagemNoTextoRepositoryTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private AsyncServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateAsyncScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	private async Task<Guid> CriarPublicacaoAsync()
	{
		await using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

		var slug = $"imgtxt-{Guid.NewGuid():N}"[..20];
		var setor = new Setor($"Setor {slug}", slug);
		contexto.Setores.Add(setor);

		var publicacao = new Publicacao(
			setor.Id, "Aviso", null, TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, DateTimeOffset.UtcNow, null, "teste");
		await escopo.ServiceProvider.GetRequiredService<IPublicacaoRepository>().AddAsync(publicacao);

		return publicacao.Id;
	}

	[Fact]
	public async Task Adicionar_DuasImagens_ListarDevolveAsDuas()
	{
		var publicacaoId = await CriarPublicacaoAsync();

		await using (var escrita = NovoEscopo())
		{
			var imagens = escrita.ServiceProvider.GetRequiredService<IImagemNoTextoRepository>();
			imagens.Adicionar(new ImagemNoTexto(publicacaoId, "a.webp", "chave-a"));
			imagens.Adicionar(new ImagemNoTexto(publicacaoId, "b.webp", "chave-b"));
			await escrita.ServiceProvider.GetRequiredService<IPublicacaoRepository>().SaveChangesAsync();
		}

		await using var leitura = NovoEscopo();
		var lista = await leitura.ServiceProvider
			.GetRequiredService<IImagemNoTextoRepository>()
			.ListarDaPublicacaoAsync(publicacaoId);

		lista.Select(imagem => imagem.CaminhoRelativo).Should().BeEquivalentTo("a.webp", "b.webp");
	}

	[Fact]
	public async Task GetAsync_ImagemDeOutraPublicacao_DevolveNulo()
	{
		var publicacaoId = await CriarPublicacaoAsync();
		var outraId = await CriarPublicacaoAsync();
		var imagem = new ImagemNoTexto(publicacaoId, "a.webp", "chave-a");

		await using (var escrita = NovoEscopo())
		{
			escrita.ServiceProvider.GetRequiredService<IImagemNoTextoRepository>().Adicionar(imagem);
			await escrita.ServiceProvider.GetRequiredService<IPublicacaoRepository>().SaveChangesAsync();
		}

		await using var leitura = NovoEscopo();
		var repositorio = leitura.ServiceProvider.GetRequiredService<IImagemNoTextoRepository>();

		(await repositorio.GetAsync(outraId, imagem.Id)).Should().BeNull();
		(await repositorio.GetAsync(publicacaoId, imagem.Id)).Should().NotBeNull();
	}

	[Fact]
	public async Task Remover_Imagem_SaiDaLista()
	{
		var publicacaoId = await CriarPublicacaoAsync();
		var imagem = new ImagemNoTexto(publicacaoId, "a.webp", "chave-a");

		await using (var escrita = NovoEscopo())
		{
			escrita.ServiceProvider.GetRequiredService<IImagemNoTextoRepository>().Adicionar(imagem);
			await escrita.ServiceProvider.GetRequiredService<IPublicacaoRepository>().SaveChangesAsync();
		}

		await using (var remocao = NovoEscopo())
		{
			var repositorio = remocao.ServiceProvider.GetRequiredService<IImagemNoTextoRepository>();
			var existente = await repositorio.GetAsync(publicacaoId, imagem.Id);
			repositorio.Remover(existente!);
			await remocao.ServiceProvider.GetRequiredService<IPublicacaoRepository>().SaveChangesAsync();
		}

		await using var leitura = NovoEscopo();
		var lista = await leitura.ServiceProvider
			.GetRequiredService<IImagemNoTextoRepository>()
			.ListarDaPublicacaoAsync(publicacaoId);

		lista.Should().BeEmpty();
	}
}
