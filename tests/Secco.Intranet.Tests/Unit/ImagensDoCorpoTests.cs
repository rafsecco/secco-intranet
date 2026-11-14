using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>O corpo com imagens no meio do texto em publicar e editar: aceita, recusa, grava e reescreve.</summary>
public class ImagensDoCorpoTests
{
	private readonly ProcessadorDeImagemFalso _processador = new();
	private readonly ArquivoStoreFalso _arquivos = new();
	private readonly ImagemNoTextoRepositorioFalso _imagens = new();
	private readonly RepositorioFalso _repositorio = new();
	private readonly TrilhaFalsa _trilha = new();

	private PublicarPublicacaoHandler Publicador() =>
		new(_repositorio, new SetorRepositorioFalso(), new IntranetOptions(), new DiretorioVazio(), new NotificadorMudo(),
			new NotificacaoOptions(), _trilha, _processador, _arquivos, _imagens,
			new LoggerCapturador<PublicarPublicacaoHandler>());

	private EditarPublicacaoHandler Editor()
	{
		_arquivos.ContadorDeSalvamentos = () => _repositorio.Salvou;

		return new(_repositorio, new IntranetOptions(), _trilha, _processador, _arquivos, _imagens,
			new LoggerCapturador<EditarPublicacaoHandler>());
	}

	private static ImagemEnviada Upload() => new(new MemoryStream([9, 9]));

	private static IReadOnlyList<ImagemEnviada> Uploads(int quantidade) =>
		[.. Enumerable.Range(0, quantidade).Select(_ => Upload())];

	private static PublicarPublicacaoCommand Publicar(
		string? corpo,
		IReadOnlyList<ImagemEnviada>? imagensDoTexto = null,
		ImagemEnviada? imagem = null) =>
		new("financeiro", "Comunicado", corpo, TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou",
			Guid.NewGuid(), imagem, null, imagensDoTexto);

	private static EditarPublicacaoCommand Editar(
		Publicacao publicacao,
		string? corpo,
		IReadOnlyList<ImagemEnviada>? imagensDoTexto = null) =>
		new(publicacao.Id, new HashSet<string>(["financeiro"]), true, "Novo", corpo, TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal, DateTimeOffset.UtcNow.AddMinutes(-1), null,
			ImagensDoTexto: imagensDoTexto);

	private Publicacao Existente(string corpo = "Texto")
	{
		var publicacao = new Publicacao(Guid.NewGuid(), "Comunicado", corpo, TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal, DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem");

		_repositorio.Existente = new PublicacaoComSetor(publicacao, "Financeiro", "financeiro");

		return publicacao;
	}

	private ImagemNoTexto Guardada(Publicacao publicacao, string caminho)
	{
		var imagem = new ImagemNoTexto(publicacao.Id, caminho, "chave-" + caminho);

		_imagens.Guardadas.Add(imagem);

		return imagem;
	}

	[Fact]
	public async Task Publicar_NovaImagemComArquivo_GravaEReescreveOMarcador()
	{
		var resultado = await Publicador().HandleAsync(Publicar("Veja ![gráfico](nova-imagem:0) agora", Uploads(1)));

		resultado.IsSuccess.Should().BeTrue();
		var nova = _imagens.Guardadas.Should().ContainSingle().Subject;
		var publicacao = _repositorio.Adicionadas.Should().ContainSingle().Subject;
		nova.PublicacaoId.Should().Be(publicacao.Id);
		publicacao.Corpo.Should().Be($"Veja ![gráfico](/publicacoes/{publicacao.Id}/imagens/{nova.Id}) agora");
		_arquivos.Gravados.Should().ContainSingle().Which.Pasta.Should().Be("mural/imagens");
	}

	[Fact]
	public async Task Publicar_NovaImagemSemArquivo_RecusaSemGravar()
	{
		var resultado = await Publicador().HandleAsync(Publicar("![a](nova-imagem:0)"));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemNoTextoSemArquivo);
		_arquivos.Gravados.Should().BeEmpty();
		_repositorio.Adicionadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Publicar_MarcadorForaDoIntervalo_RecusaSemArquivo()
	{
		var resultado = await Publicador().HandleAsync(Publicar("![a](nova-imagem:1)", Uploads(1)));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemNoTextoSemArquivo);
		_arquivos.Gravados.Should().BeEmpty();
	}

	[Theory]
	[InlineData("![a](/outro/caminho.png)")]
	[InlineData("![a](imagem.png)")]
	[InlineData("![a](file:///c:/segredo.png)")]
	[InlineData("![a](blob:https://x/abc)")]
	[InlineData("![a](data:image/png;base64,AAAA)")]
	[InlineData("![a](pendente:1)")]
	[InlineData("![a](javascript:alert(1))")]
	[InlineData("![a](nova-imagem:abc)")]
	[InlineData("![a](//outro.com/x.png)")]
	public async Task Publicar_DestinoNaoPermitido_Recusa(string corpo)
	{
		var resultado = await Publicador().HandleAsync(Publicar(corpo, Uploads(1)));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemNoTextoInvalida);
		_arquivos.Gravados.Should().BeEmpty();
		_repositorio.Adicionadas.Should().BeEmpty();
	}

	[Theory]
	[InlineData("![a](https://exemplo.com/x.png)")]
	[InlineData("![a](http://exemplo.com/x.png)")]
	public async Task Publicar_ImagemHttp_AceitaSemReescrever(string corpo)
	{
		var resultado = await Publicador().HandleAsync(Publicar(corpo));

		resultado.IsSuccess.Should().BeTrue();
		_repositorio.Adicionadas.Single().Corpo.Should().Be(corpo);
		_arquivos.Gravados.Should().BeEmpty();
	}

	[Fact]
	public async Task Publicar_ImagemExistenteInventadaNaCriacao_Recusa()
	{
		var corpo = $"![a](/publicacoes/{Guid.NewGuid()}/imagens/{Guid.NewGuid()})";

		var resultado = await Publicador().HandleAsync(Publicar(corpo));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemNoTextoInvalida);
	}

	[Fact]
	public async Task Publicar_DuasImagensNovas_ReescreveOsMarcadores()
	{
		var resultado = await Publicador().HandleAsync(Publicar("![a](nova-imagem:0) e ![b](nova-imagem:1)", Uploads(2)));

		resultado.IsSuccess.Should().BeTrue();
		var corpo = _repositorio.Adicionadas.Single().Corpo;
		corpo.Should().NotContain("nova-imagem:");
		_imagens.Guardadas.Should().HaveCount(2);
		_imagens.Guardadas[0].Id.Should().NotBe(_imagens.Guardadas[1].Id);
		corpo.Should().Contain($"/imagens/{_imagens.Guardadas[0].Id})").And.Contain($"/imagens/{_imagens.Guardadas[1].Id})");
	}

	[Fact]
	public async Task Publicar_MarcadorRepetido_GravaUmaImagemSo()
	{
		var resultado = await Publicador().HandleAsync(Publicar("![a](nova-imagem:0) ![b](nova-imagem:0)", Uploads(1)));

		resultado.IsSuccess.Should().BeTrue();
		_arquivos.Gravados.Should().ContainSingle();
		_imagens.Guardadas.Should().ContainSingle();
		var caminho = $"/imagens/{_imagens.Guardadas[0].Id})";
		_repositorio.Adicionadas.Single().Corpo.Split(caminho).Should().HaveCount(3);
	}

	[Fact]
	public async Task Publicar_ArquivoSemMarcador_NaoGrava()
	{
		var resultado = await Publicador().HandleAsync(Publicar("Só texto", Uploads(2)));

		resultado.IsSuccess.Should().BeTrue();
		_arquivos.Gravados.Should().BeEmpty();
		_imagens.Guardadas.Should().BeEmpty();
		_processador.Chamadas.Should().Be(0);
	}

	[Fact]
	public async Task Publicar_SegundaImagemInvalida_ApagaAPrimeira()
	{
		_processador.FalharNaChamada = 2;
		_processador.FalharCom = IntranetErrors.Publicacoes.ImagemInvalida;

		var resultado = await Publicador().HandleAsync(Publicar("![a](nova-imagem:0) ![b](nova-imagem:1)", Uploads(2)));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemInvalida);
		_arquivos.Gravados.Should().ContainSingle();
		_arquivos.Removidos.Should().ContainSingle().Which.Should().Be(_arquivos.Gravados[0].Caminho);
		_repositorio.Adicionadas.Should().BeEmpty();
		_imagens.Guardadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Publicar_OnzeImagens_Recusa()
	{
		var corpo = string.Join(' ', Enumerable.Range(0, 11).Select(n => $"![a](nova-imagem:{n})"));

		var resultado = await Publicador().HandleAsync(Publicar(corpo, Uploads(11)));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagensNoTextoDemais);
		_arquivos.Gravados.Should().BeEmpty();
	}

	[Fact]
	public async Task Publicar_DezImagens_Aceita()
	{
		var corpo = string.Join(' ', Enumerable.Range(0, 10).Select(n => $"![a](nova-imagem:{n})"));

		var resultado = await Publicador().HandleAsync(Publicar(corpo, Uploads(10)));

		resultado.IsSuccess.Should().BeTrue();
		_imagens.Guardadas.Should().HaveCount(10);
	}

	[Fact]
	public async Task Publicar_ComImagemFinalEImagensNoTexto_FalhaDeSalvarApagaTodas()
	{
		_repositorio.FalhaAoAdicionar = new InvalidOperationException("banco fora");

		var acao = () => Publicador().HandleAsync(Publicar("![a](nova-imagem:0)", Uploads(1), Upload()));

		await acao.Should().ThrowAsync<InvalidOperationException>();
		_arquivos.Gravados.Should().HaveCount(2);
		_arquivos.Removidos.Should().BeEquivalentTo(_arquivos.Gravados.Select(g => g.Caminho));
	}

	[Fact]
	public async Task Publicar_ImagemDoTextoFalha_ApagaAImagemFinal()
	{
		_processador.FalharNaChamada = 2;
		_processador.FalharCom = IntranetErrors.Publicacoes.ImagemInvalida;

		var resultado = await Publicador().HandleAsync(Publicar("![a](nova-imagem:0)", Uploads(1), Upload()));

		resultado.IsFailure.Should().BeTrue();
		_arquivos.Removidos.Should().BeEquivalentTo(_arquivos.Gravados.Select(g => g.Caminho));
		_arquivos.Gravados.Should().ContainSingle();
	}

	[Fact]
	public async Task Publicar_Audita_QuantidadeDeImagensNoTexto()
	{
		await Publicador().HandleAsync(Publicar("![a](nova-imagem:0) ![b](nova-imagem:1)", Uploads(2)));

		var registro = _trilha.Registros.Should().ContainSingle().Subject;
		System.Text.Json.JsonDocument.Parse(registro.Metadata!).RootElement
			.GetProperty("imagensNoTexto").GetInt32().Should().Be(2);
	}

	[Fact]
	public async Task Editar_ImagemRemovidaDoCorpo_ApagaDepoisDeSalvar()
	{
		var publicacao = Existente();
		var imagem = Guardada(publicacao, "mural/imagens/a.bin");

		var resultado = await Editor().HandleAsync(Editar(publicacao, "Sem imagem agora"));

		resultado.IsSuccess.Should().BeTrue();
		_imagens.Removidas.Should().ContainSingle().Which.Should().BeSameAs(imagem);
		_arquivos.Removidos.Should().ContainSingle().Which.Should().Be("mural/imagens/a.bin");
		_arquivos.SalvamentosNasRemocoes.Should().ContainSingle().Which.Should().Be(1);
	}

	[Fact]
	public async Task Editar_SemMudarNada_MantemAsImagens()
	{
		var publicacao = Existente();
		var imagem = Guardada(publicacao, "mural/imagens/a.bin");
		var corpo = $"![a](/publicacoes/{publicacao.Id}/imagens/{imagem.Id})";

		var resultado = await Editor().HandleAsync(Editar(publicacao, corpo));

		resultado.IsSuccess.Should().BeTrue();
		_imagens.Removidas.Should().BeEmpty();
		_arquivos.Removidos.Should().BeEmpty();
		publicacao.Corpo.Should().Be(corpo);
	}

	[Fact]
	public async Task Editar_ImagemDeOutraPublicacao_Recusa()
	{
		var publicacao = Existente();
		var outra = Guardada(new Publicacao(Guid.NewGuid(), "Outra", "x", TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem"), "mural/imagens/outra.bin");
		var corpo = $"![a](/publicacoes/{publicacao.Id}/imagens/{outra.Id})";

		var resultado = await Editor().HandleAsync(Editar(publicacao, corpo));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemNoTextoInvalida);
		_repositorio.Salvou.Should().Be(0);
	}

	[Fact]
	public async Task Editar_RotaDeOutraPublicacaoComImagemMinha_Recusa()
	{
		var publicacao = Existente();
		var imagem = Guardada(publicacao, "mural/imagens/a.bin");
		var corpo = $"![a](/publicacoes/{Guid.NewGuid()}/imagens/{imagem.Id})";

		var resultado = await Editor().HandleAsync(Editar(publicacao, corpo));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemNoTextoInvalida);
	}

	[Fact]
	public async Task Editar_NovaImagemMaisExistente_GravaMantemEAudita()
	{
		var publicacao = Existente();
		var antiga = Guardada(publicacao, "mural/imagens/a.bin");
		var corpo = $"![a](/publicacoes/{publicacao.Id}/imagens/{antiga.Id}) ![b](nova-imagem:0)";

		var resultado = await Editor().HandleAsync(Editar(publicacao, corpo, Uploads(1)));

		resultado.IsSuccess.Should().BeTrue();
		_imagens.Guardadas.Should().HaveCount(2);
		publicacao.Corpo.Should().NotContain("nova-imagem:");
		_arquivos.Removidos.Should().BeEmpty();
		System.Text.Json.JsonDocument.Parse(_trilha.Registros.Single().Metadata!).RootElement
			.GetProperty("imagensNoTexto").GetInt32().Should().Be(2);
	}

	[Fact]
	public async Task Editar_SalvarFalha_ApagaAsNovasENaoAsAntigas()
	{
		var publicacao = Existente();
		var antiga = Guardada(publicacao, "mural/imagens/a.bin");
		_repositorio.FalhaAoSalvar = new InvalidOperationException("banco fora");

		var acao = () => Editor().HandleAsync(Editar(publicacao, "![b](nova-imagem:0)", Uploads(1)));

		await acao.Should().ThrowAsync<InvalidOperationException>();
		_arquivos.Gravados.Should().ContainSingle();
		_arquivos.Removidos.Should().ContainSingle().Which.Should().Be(_arquivos.Gravados[0].Caminho);
		_arquivos.Removidos.Should().NotContain(antiga.CaminhoRelativo);
	}

	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class DiretorioVazio : IDiretorioDeUsuarios
	{
		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
			CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<UsuarioDoTenant>>([]);
	}

	private sealed class NotificadorMudo : INotificadorDeMensagens
	{
		public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;
	}

	private sealed class RepositorioFalso : IPublicacaoRepository
	{
		public List<Publicacao> Adicionadas { get; } = [];

		public PublicacaoComSetor? Existente { get; set; }

		public int Salvou { get; private set; }

		public Exception? FalhaAoAdicionar { get; set; }

		public Exception? FalhaAoSalvar { get; set; }

		public Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default)
		{
			if (FalhaAoAdicionar is not null)
			{
				throw FalhaAoAdicionar;
			}

			Adicionadas.Add(publicacao);

			return Task.CompletedTask;
		}

		public Task SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			if (FalhaAoSalvar is not null)
			{
				throw FalhaAoSalvar;
			}

			Salvou++;

			return Task.CompletedTask;
		}

		public Task<PublicacaoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(Existente);

		public Task<PagedResult<PublicacaoDto>> ListarNoMuralAsync(
			MuralCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Create<PublicacaoDto>([], criteria.Page, 0));

		public Task<IReadOnlyList<PublicacaoDto>> ListarDoSetorAsync(
			string setorSlug, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<PublicacaoDto>>([]);
	}

	private sealed class SetorRepositorioFalso : ISetorRepository
	{
		public Task AddAsync(Setor setor, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<Setor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<Setor?> GetParaEdicaoAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(null);

		public Task<Setor?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult<Setor?>(new Setor("Financeiro", "financeiro"));

		public Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
			Task.FromResult(true);

		public Task<PagedResult<Setor>> SearchAsync(
			SetorSearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Empty<Setor>(new PageRequest(1)));

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
