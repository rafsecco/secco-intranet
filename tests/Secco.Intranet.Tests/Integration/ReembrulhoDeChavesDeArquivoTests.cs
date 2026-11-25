using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Infrastructure.Armazenamento;
using Secco.Intranet.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Cryptography;
using Xunit;

namespace Secco.Intranet.Tests.Integration;

/// <summary>
/// O reembrulho das chaves de arquivo contra o banco de verdade: as quatro colunas, o que fica
/// de fora e a gravação condicionada que protege contra a corrida com uma troca de arquivo.
/// </summary>
public class ReembrulhoDeChavesDeArquivoTests(IntranetWebFactory factory) : IClassFixture<IntranetWebFactory>, IAsyncLifetime
{
	private sealed class AmbienteFalso : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = "Production";

		public string ApplicationName { get; set; } = "Secco.Intranet.Tests";

		public string ContentRootPath { get; set; } = string.Empty;

		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}

	private static readonly byte[] Conteudo = "conteudo conhecido do arquivo"u8.ToArray();

	private readonly string _chaveA = NovaChave();
	private readonly string _chaveB = NovaChave();
	private readonly string _chaveC = NovaChave();

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		// As linhas dos testes anteriores têm chaves de outros cifradores e sujariam as contagens.
		using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
		await contexto.ImagensNoTexto.ExecuteDeleteAsync();
		await contexto.Publicacoes.ExecuteDeleteAsync();
		await contexto.Documentos.ExecuteDeleteAsync();
		await contexto.PerfisColaboradores.ExecuteDeleteAsync();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string NovaChave() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

	private static ISeccoFileCipher Cifrador(string ativa, params string[] aposentadas) =>
		CifradorDeArquivos.Criar(
			new ChaveMestraOptions { ChaveAtiva = ativa, ChavesAposentadas = [.. aposentadas] },
			new AmbienteFalso());

	private IServiceScope NovoEscopo()
	{
		var escopo = factory.Services.CreateScope();
		escopo.ServiceProvider.SetTenant(factory.TenantAlfa);

		return escopo;
	}

	private static async Task<(string Chave, byte[] Cifrado)> CifrarAsync(ISeccoFileCipher cifrador)
	{
		using var origem = new MemoryStream(Conteudo);
		using var destino = new MemoryStream();
		var resultado = await cifrador.EncryptAsync(origem, destino);

		return (resultado.WrappedKey, destino.ToArray());
	}

	private static async Task<byte[]> DecifrarAsync(ISeccoFileCipher cifrador, byte[] cifrado, string chave)
	{
		using var origem = new MemoryStream(cifrado);
		using var destino = new MemoryStream();
		await cifrador.DecryptAsync(origem, destino, chave);

		return destino.ToArray();
	}

	private sealed record Linhas(Guid DocumentoId, Guid PublicacaoId, Guid ImagemId, Guid PerfilId);

	private sealed record Chaves(string? Documento, string? Publicacao, string? Imagem, string? Perfil);

	/// <summary>Uma linha em cada tabela, todas com a mesma chave embrulhada.</summary>
	private async Task<Linhas> CriarLinhasAsync(string chave)
	{
		using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

		var slug = $"reemb-{Guid.NewGuid():N}"[..20];
		var setor = new Setor($"Setor {slug}", slug);
		contexto.Setores.Add(setor);

		var documento = new Documento(
			DonoDeDocumento.DoSetor(setor.Id), "Doc", null, "a.pdf", "application/pdf", 10,
			Visibilidade.Setor, $"t/{Guid.NewGuid():N}", chave, "quem", null, 0);
		contexto.Documentos.Add(documento);

		var publicacao = new Publicacao(
			setor.Id, "Aviso", null, TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, DateTimeOffset.UtcNow, null, "teste");
		publicacao.DefinirImagem($"p/{Guid.NewGuid():N}", chave, null);
		contexto.Publicacoes.Add(publicacao);

		var imagem = new ImagemNoTexto(publicacao.Id, $"i/{Guid.NewGuid():N}", chave);
		contexto.ImagensNoTexto.Add(imagem);

		var perfil = new PerfilColaborador(Guid.NewGuid());
		perfil.DefinirFoto($"f/{Guid.NewGuid():N}", chave);
		contexto.PerfisColaboradores.Add(perfil);

		await contexto.SaveChangesAsync();

		return new Linhas(documento.Id, publicacao.Id, imagem.Id, perfil.Id);
	}

	private async Task<Chaves> LerChavesAsync(Linhas linhas)
	{
		using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

		return new Chaves(
			(await contexto.Documentos.AsNoTracking().SingleAsync(d => d.Id == linhas.DocumentoId)).ChaveEmbrulhada,
			(await contexto.Publicacoes.AsNoTracking().SingleAsync(p => p.Id == linhas.PublicacaoId)).ImagemChaveEmbrulhada,
			(await contexto.ImagensNoTexto.AsNoTracking().SingleAsync(i => i.Id == linhas.ImagemId)).ChaveEmbrulhada,
			(await contexto.PerfisColaboradores.AsNoTracking().SingleAsync(p => p.Id == linhas.PerfilId)).FotoChaveEmbrulhada);
	}

	private async Task<ResultadoDoReembrulho> ReembrulharAsync(ISeccoFileCipher cifrador)
	{
		using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

		return await new ReembrulhoDeChavesDeArquivo(cifrador).ReembrulharAsync(contexto);
	}

	[Fact]
	public async Task ReembrulharAsync_ChavesDaAposentada_ReembrulhaAsQuatroColunas()
	{
		var (chaveA, cifrado) = await CifrarAsync(Cifrador(_chaveA));
		var linhas = await CriarLinhasAsync(chaveA);
		var cifradorB = Cifrador(_chaveB, _chaveA);

		var resultado = await ReembrulharAsync(cifradorB);

		resultado.Reembrulhadas.Should().Be(4);
		resultado.SemChaveConhecida.Should().Be(0);

		var chaves = await LerChavesAsync(linhas);
		var soB = Cifrador(_chaveB);
		foreach (var chave in new[] { chaves.Documento, chaves.Publicacao, chaves.Imagem, chaves.Perfil })
		{
			cifradorB.IsKeyWrappedWithActiveKey(chave!).Should().BeTrue();
			(await DecifrarAsync(soB, cifrado, chave!)).Should().Equal(Conteudo);
		}
	}

	[Fact]
	public async Task ReembrulharAsync_SegundaPassada_NaoMudaNada()
	{
		var (chaveA, _) = await CifrarAsync(Cifrador(_chaveA));
		var linhas = await CriarLinhasAsync(chaveA);
		var cifradorB = Cifrador(_chaveB, _chaveA);

		await ReembrulharAsync(cifradorB);
		var aposPrimeira = await LerChavesAsync(linhas);

		var segunda = await ReembrulharAsync(cifradorB);

		segunda.Reembrulhadas.Should().Be(0);
		segunda.SemChaveConhecida.Should().Be(0);
		(await LerChavesAsync(linhas)).Should().Be(aposPrimeira);
	}

	[Fact]
	public async Task ReembrulharAsync_ChaveSemChaveConhecida_ContaESegue()
	{
		var (chaveA, _) = await CifrarAsync(Cifrador(_chaveA));
		var (chaveC, _) = await CifrarAsync(Cifrador(_chaveC));
		var comA = await CriarLinhasAsync(chaveA);
		var comC = await CriarLinhasAsync(chaveC);
		var cifradorB = Cifrador(_chaveB, _chaveA);

		var resultado = await ReembrulharAsync(cifradorB);

		resultado.SemChaveConhecida.Should().Be(4);
		resultado.Reembrulhadas.Should().Be(4);

		(await LerChavesAsync(comC)).Should().Be(new Chaves(chaveC, chaveC, chaveC, chaveC));

		var lidaA = await LerChavesAsync(comA);
		cifradorB.IsKeyWrappedWithActiveKey(lidaA.Documento!).Should().BeTrue();
		cifradorB.IsKeyWrappedWithActiveKey(lidaA.Perfil!).Should().BeTrue();
	}

	[Fact]
	public async Task ReembrulharAsync_DocumentoArquivadoEGrupo()
	{
		var (chaveA, _) = await CifrarAsync(Cifrador(_chaveA));
		Guid arquivadoId;
		Guid grupoId;

		using (var escopo = NovoEscopo())
		{
			var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
			var slug = $"reemb-{Guid.NewGuid():N}"[..20];
			var setor = new Setor($"Setor {slug}", slug);
			contexto.Setores.Add(setor);

			var arquivado = new Documento(
				DonoDeDocumento.DoSetor(setor.Id), "Velho", null, "a.pdf", "application/pdf", 10,
				Visibilidade.Setor, $"t/{Guid.NewGuid():N}", chaveA, "quem", null, 0);
			arquivado.Arquivar();
			var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(setor.Id), "Grupo", null, "quem");
			contexto.Documentos.AddRange(arquivado, grupo);
			await contexto.SaveChangesAsync();

			arquivadoId = arquivado.Id;
			grupoId = grupo.Id;
		}

		var cifradorB = Cifrador(_chaveB, _chaveA);
		var resultado = await ReembrulharAsync(cifradorB);

		resultado.Reembrulhadas.Should().Be(1);
		resultado.SemChaveConhecida.Should().Be(0);

		using var leitura = NovoEscopo();
		var lido = leitura.ServiceProvider.GetRequiredService<IntranetDbContext>();
		var depoisArquivado = await lido.Documentos.AsNoTracking().SingleAsync(d => d.Id == arquivadoId);
		var depoisGrupo = await lido.Documentos.AsNoTracking().SingleAsync(d => d.Id == grupoId);

		depoisArquivado.Ativo.Should().BeFalse();
		cifradorB.IsKeyWrappedWithActiveKey(depoisArquivado.ChaveEmbrulhada!).Should().BeTrue();
		depoisGrupo.ChaveEmbrulhada.Should().BeNull();
	}

	[Fact]
	public async Task ReembrulharAsync_ImagemNoTextoComChaveVazia_Ignora()
	{
		Guid imagemId;

		using (var escopo = NovoEscopo())
		{
			var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();
			var slug = $"reemb-{Guid.NewGuid():N}"[..20];
			var setor = new Setor($"Setor {slug}", slug);
			contexto.Setores.Add(setor);
			var publicacao = new Publicacao(
				setor.Id, "Aviso", null, TipoPublicacao.Aviso, Visibilidade.Empresa,
				PrioridadePublicacao.Normal, DateTimeOffset.UtcNow, null, "teste");
			contexto.Publicacoes.Add(publicacao);
			var imagem = new ImagemNoTexto(publicacao.Id, $"i/{Guid.NewGuid():N}", "x");
			contexto.ImagensNoTexto.Add(imagem);
			await contexto.SaveChangesAsync();

			// O construtor recusa chave vazia; o banco, não. A linha vazia vem de dado antigo.
			await contexto.ImagensNoTexto.Where(i => i.Id == imagem.Id)
				.ExecuteUpdateAsync(s => s.SetProperty(i => EF.Property<string>(i, "ChaveEmbrulhada"), string.Empty));
			imagemId = imagem.Id;
		}

		var resultado = await ReembrulharAsync(Cifrador(_chaveB, _chaveA));

		resultado.Reembrulhadas.Should().Be(0);
		resultado.SemChaveConhecida.Should().Be(0);

		using var leitura = NovoEscopo();
		var depois = await leitura.ServiceProvider.GetRequiredService<IntranetDbContext>()
			.ImagensNoTexto.AsNoTracking().SingleAsync(i => i.Id == imagemId);
		depois.ChaveEmbrulhada.Should().BeEmpty();
	}

	[Fact]
	public async Task TrocarSeAindaForAsync_ChaveMudouDepoisDaLeitura_NaoSobrescreve()
	{
		var (chaveA, _) = await CifrarAsync(Cifrador(_chaveA));
		var (chaveX, _) = await CifrarAsync(Cifrador(_chaveC));
		var (chaveNova, _) = await CifrarAsync(Cifrador(_chaveB));
		var linhas = await CriarLinhasAsync(chaveA);

		using var escopo = NovoEscopo();
		var contexto = escopo.ServiceProvider.GetRequiredService<IntranetDbContext>();

		// Entre a leitura e a gravação, outro processo trocou o arquivo do perfil.
		await contexto.PerfisColaboradores.Where(p => p.Id == linhas.PerfilId)
			.ExecuteUpdateAsync(s => s.SetProperty(p => p.FotoChaveEmbrulhada, chaveX));

		var perdeu = await ReembrulhoDeChavesDeArquivo.TrocarSeAindaForAsync<PerfilColaborador>(
			contexto, nameof(PerfilColaborador.FotoChaveEmbrulhada), linhas.PerfilId, chaveA, chaveNova, CancellationToken.None);

		perdeu.Should().BeFalse();
		(await contexto.PerfisColaboradores.AsNoTracking().SingleAsync(p => p.Id == linhas.PerfilId))
			.FotoChaveEmbrulhada.Should().Be(chaveX);

		var ganhou = await ReembrulhoDeChavesDeArquivo.TrocarSeAindaForAsync<PerfilColaborador>(
			contexto, nameof(PerfilColaborador.FotoChaveEmbrulhada), linhas.PerfilId, chaveX, chaveNova, CancellationToken.None);

		ganhou.Should().BeTrue();
		(await contexto.PerfisColaboradores.AsNoTracking().SingleAsync(p => p.Id == linhas.PerfilId))
			.FotoChaveEmbrulhada.Should().Be(chaveNova);
	}
}
