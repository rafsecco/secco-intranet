using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
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

/// <summary>Imagem da publicação em publicar e editar: processa, guarda e troca sem deixar linha sem arquivo.</summary>
public class ImagemDaPublicacaoHandlersTests
{
	private readonly ProcessadorDeImagemFalso _imagens = new();
	private readonly ArquivoStoreFalso _arquivos = new();
	private readonly RepositorioEmMemoria _repositorio = new();
	private readonly TrilhaFalsa _trilha = new();
	private readonly NotificadorFalso _notificador = new();
	private readonly LoggerCapturador<EditarPublicacaoHandler> _logger = new();

	private PublicarPublicacaoHandler Publicador() =>
		new(_repositorio, new SetorRepositorioFalso(), new IntranetOptions(),
			new DiretorioFalso(), _notificador, new NotificacaoOptions(), _trilha,
			_imagens, _arquivos, new LoggerCapturador<PublicarPublicacaoHandler>());

	private EditarPublicacaoHandler Editor()
	{
		_arquivos.ContadorDeSalvamentos = () => _repositorio.Salvou;

		return new(_repositorio, new IntranetOptions(), _trilha, _imagens, _arquivos, _logger);
	}

	private static PublicarPublicacaoCommand Publicar(
		string? corpo = "Texto",
		ImagemEnviada? imagem = null,
		string? descricao = null) =>
		new("financeiro", "Comunicado", corpo, TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem.publicou",
			Guid.NewGuid(), imagem, descricao);

	private static EditarPublicacaoCommand Editar(
		Publicacao publicacao,
		ImagemEnviada? imagem = null,
		string? descricao = null,
		bool remover = false) =>
		new(publicacao.Id, new HashSet<string>(["financeiro"]), true, "Novo", "Texto", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal, DateTimeOffset.UtcNow.AddMinutes(-1), null,
			imagem, descricao, remover);

	private static ImagemEnviada Upload() => new(new MemoryStream([9, 9]));

	private Publicacao ComImagem(string caminho = "mural/imagens/antiga.bin", string? descricao = "Descrição antiga")
	{
		var publicacao = new Publicacao(Guid.NewGuid(), "Comunicado", "Texto", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal, DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem");

		publicacao.DefinirImagem(caminho, "chave-antiga", descricao);
		_repositorio.Existente = new PublicacaoComSetor(publicacao, "Financeiro", "financeiro");

		return publicacao;
	}

	private Publicacao SemImagem()
	{
		var publicacao = new Publicacao(Guid.NewGuid(), "Comunicado", "Texto", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal, DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem");

		_repositorio.Existente = new PublicacaoComSetor(publicacao, "Financeiro", "financeiro");

		return publicacao;
	}

	private static JsonElement Metadata(RegistroDeAuditoria registro) => JsonDocument.Parse(registro.Metadata!).RootElement;

	[Fact]
	public async Task Publicar_ComImagem_ProcessaGravaNaPastaDoMuralEDefine()
	{
		var resultado = await Publicador().HandleAsync(Publicar(imagem: Upload(), descricao: "Cartaz da campanha"));

		resultado.IsSuccess.Should().BeTrue();
		_imagens.Chamadas.Should().Be(1);
		_arquivos.Gravados.Should().ContainSingle().Which.Pasta.Should().Be("mural/imagens");
		resultado.Value.Publicacao.TemImagem.Should().BeTrue();
		resultado.Value.Publicacao.ImagemDescricao.Should().Be("Cartaz da campanha");
		_repositorio.Adicionadas.Should().ContainSingle().Which.ImagemCaminhoRelativo
			.Should().Be(_arquivos.Gravados[0].Caminho);
	}

	[Fact]
	public async Task Publicar_ImagemRecusada_NaoGravaNada()
	{
		_imagens.FalharCom = IntranetErrors.Publicacoes.ImagemFormatoNaoSuportado;

		var resultado = await Publicador().HandleAsync(Publicar(imagem: Upload()));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemFormatoNaoSuportado);
		_arquivos.Gravados.Should().BeEmpty();
		_repositorio.Adicionadas.Should().BeEmpty();
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Publicar_DescricaoAcimaDoLimite_RecusaAntesDeProcessar()
	{
		var resultado = await Publicador().HandleAsync(Publicar(imagem: Upload(), descricao: new string('a', 301)));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemDescricaoTooLong);
		_imagens.Chamadas.Should().Be(0);
		_arquivos.Gravados.Should().BeEmpty();
		_repositorio.Adicionadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Publicar_DescricaoSemImagem_Ignora()
	{
		var resultado = await Publicador().HandleAsync(Publicar(descricao: "Sem imagem"));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Publicacao.TemImagem.Should().BeFalse();
		resultado.Value.Publicacao.ImagemDescricao.Should().BeNull();
	}

	[Fact]
	public async Task Publicar_SemTextoSemImagem_Aceita()
	{
		var resultado = await Publicador().HandleAsync(Publicar(corpo: null));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Publicacao.Corpo.Should().BeEmpty();
		resultado.Value.Publicacao.TemImagem.Should().BeFalse();
		_repositorio.Adicionadas.Should().ContainSingle();
	}

	[Fact]
	public async Task Publicar_SemTexto_AvisaComMensagemPadrao()
	{
		var resultado = await Publicador().HandleAsync(Publicar(corpo: null, imagem: Upload()));

		resultado.IsSuccess.Should().BeTrue();
		_notificador.Lotes.Should().NotBeEmpty();
		_notificador.Lotes.Should().OnlyContain(l => l.Resumo == "Abra a publicação na intranet para ver.");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Publicar_Audita_ImagemTrueOuFalse(bool comImagem)
	{
		await Publicador().HandleAsync(Publicar(imagem: comImagem ? Upload() : null));

		var registro = _trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.MuralPublicar);
		Metadata(registro).GetProperty("imagem").GetBoolean().Should().Be(comImagem);
	}

	[Fact]
	public async Task Publicar_StoreFalha_NaoGravaLinha()
	{
		_arquivos.FalhaAoGravar = new IOException("disco cheio");

		var acao = () => Publicador().HandleAsync(Publicar(imagem: Upload()));

		await acao.Should().ThrowAsync<IOException>();
		_repositorio.Adicionadas.Should().BeEmpty();
		_repositorio.Salvou.Should().Be(0);
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_SemArquivoNovo_MantemImagemENaoApaga()
	{
		var publicacao = ComImagem();

		var resultado = await Editor().HandleAsync(Editar(publicacao));

		resultado.IsSuccess.Should().BeTrue();
		publicacao.ImagemCaminhoRelativo.Should().Be("mural/imagens/antiga.bin");
		resultado.Value.TemImagem.Should().BeTrue();
		_imagens.Chamadas.Should().Be(0);
		_arquivos.Gravados.Should().BeEmpty();
		_arquivos.Removidos.Should().BeEmpty();
		var metadata = Metadata(_trilha.Registros.Should().ContainSingle().Subject);
		metadata.GetProperty("imagem").GetBoolean().Should().BeTrue();
		metadata.GetProperty("imagemAcao").GetString().Should().Be("manter");
	}

	[Fact]
	public async Task Editar_SemArquivoNovo_AplicaADescricaoInformada()
	{
		var publicacao = ComImagem();

		await Editor().HandleAsync(Editar(publicacao, descricao: "Nova descrição"));

		publicacao.ImagemDescricao.Should().Be("Nova descrição");
		publicacao.ImagemCaminhoRelativo.Should().Be("mural/imagens/antiga.bin");
	}

	[Fact]
	public async Task Editar_DescricaoSemImagem_Ignora()
	{
		var publicacao = SemImagem();

		var resultado = await Editor().HandleAsync(Editar(publicacao, descricao: "Solta"));

		resultado.IsSuccess.Should().BeTrue();
		publicacao.ImagemDescricao.Should().BeNull();
		Metadata(_trilha.Registros[0]).GetProperty("imagem").GetBoolean().Should().BeFalse();
	}

	[Fact]
	public async Task Editar_DescricaoAcimaDoLimite_RecusaAntesDeProcessar()
	{
		var publicacao = ComImagem();

		var resultado = await Editor().HandleAsync(Editar(publicacao, Upload(), new string('a', 301)));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemDescricaoTooLong);
		_imagens.Chamadas.Should().Be(0);
		_arquivos.Gravados.Should().BeEmpty();
		_repositorio.Salvou.Should().Be(0);
	}

	[Fact]
	public async Task Editar_ArquivoNovo_TrocaEApagaOAnteriorDepoisDeSalvar()
	{
		var publicacao = ComImagem();

		var resultado = await Editor().HandleAsync(Editar(publicacao, Upload(), "Nova"));

		resultado.IsSuccess.Should().BeTrue();
		_arquivos.Gravados.Should().ContainSingle().Which.Pasta.Should().Be("mural/imagens");
		publicacao.ImagemCaminhoRelativo.Should().Be(_arquivos.Gravados[0].Caminho);
		publicacao.ImagemDescricao.Should().Be("Nova");
		_arquivos.Removidos.Should().ContainSingle().Which.Should().Be("mural/imagens/antiga.bin");
		_arquivos.SalvamentosNasRemocoes.Should().ContainSingle().Which.Should().BePositive();
		Metadata(_trilha.Registros.Should().ContainSingle().Subject).GetProperty("imagemAcao").GetString()
			.Should().Be("trocar");
	}

	[Fact]
	public async Task Editar_ImagemRecusada_NaoMudaNada()
	{
		var publicacao = ComImagem();
		_imagens.FalharCom = IntranetErrors.Publicacoes.ImagemInvalida;

		var resultado = await Editor().HandleAsync(Editar(publicacao, Upload()));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.ImagemInvalida);
		publicacao.ImagemCaminhoRelativo.Should().Be("mural/imagens/antiga.bin");
		publicacao.Titulo.Should().Be("Comunicado");
		_arquivos.Gravados.Should().BeEmpty();
		_arquivos.Removidos.Should().BeEmpty();
		_repositorio.Salvou.Should().Be(0);
	}

	[Fact]
	public async Task Editar_RemoverImagem_ApagaEAudita()
	{
		var publicacao = ComImagem();

		var resultado = await Editor().HandleAsync(Editar(publicacao, remover: true));

		resultado.IsSuccess.Should().BeTrue();
		publicacao.TemImagem.Should().BeFalse();
		resultado.Value.TemImagem.Should().BeFalse();
		_arquivos.Removidos.Should().ContainSingle().Which.Should().Be("mural/imagens/antiga.bin");
		_arquivos.SalvamentosNasRemocoes.Should().ContainSingle().Which.Should().BePositive();
		var metadata = Metadata(_trilha.Registros.Should().ContainSingle().Subject);
		metadata.GetProperty("imagem").GetBoolean().Should().BeFalse();
		metadata.GetProperty("imagemAcao").GetString().Should().Be("remover");
	}

	[Fact]
	public async Task Editar_ApagarAnteriorFalha_GravaELogaAviso()
	{
		var publicacao = ComImagem();
		_arquivos.FalharAoRemover = true;

		var resultado = await Editor().HandleAsync(Editar(publicacao, Upload()));

		resultado.IsSuccess.Should().BeTrue();
		_repositorio.Salvou.Should().BePositive();
		_trilha.Registros.Should().ContainSingle();
		var aviso = _logger.Entradas.Should().ContainSingle().Subject;
		aviso.Nivel.Should().Be(LogLevel.Warning);
		aviso.Mensagem.Should().Contain(nameof(IOException));
		aviso.Mensagem.Should().NotContain("antiga.bin");
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

	private sealed class NotificadorFalso : INotificadorDeMensagens
	{
		public List<MensagemParaEnviar> Lotes { get; } = [];

		public Task EnviarLoteAsync(MensagemParaEnviar mensagem, CancellationToken cancellationToken = default)
		{
			Lotes.Add(mensagem);

			return Task.CompletedTask;
		}
	}

	private sealed class DiretorioFalso : IDiretorioDeUsuarios
	{
		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(
			CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<UsuarioDoTenant>>(
				[new UsuarioDoTenant(Guid.NewGuid(), "a@x.com", ["financeiro-user"])]);
	}

	private sealed class RepositorioEmMemoria : IPublicacaoRepository
	{
		public List<Publicacao> Adicionadas { get; } = [];

		public PublicacaoComSetor? Existente { get; set; }

		public int Salvou { get; private set; }

		public Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default)
		{
			Adicionadas.Add(publicacao);

			return Task.CompletedTask;
		}

		public Task SaveChangesAsync(CancellationToken cancellationToken = default)
		{
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
