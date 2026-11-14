using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Pagination;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Entrega da imagem no texto: a mesma regra de quem vê da imagem final do aviso.</summary>
public class ImagemNoTextoHandlersTests
{
	private static readonly IReadOnlySet<string> SemEscrita = new HashSet<string>();

	private readonly ImagemNoTextoRepositorioFalso _imagens = new();

	private (Publicacao Publicacao, ImagemNoTexto Imagem, ObterImagemNoTextoHandler Handler) Montar(
		Visibilidade visibilidade = Visibilidade.Empresa,
		bool agendada = false,
		bool arquivada = false)
	{
		var publicacao = new Publicacao(Guid.NewGuid(), "Comunicado", "Texto", TipoPublicacao.Aviso,
			visibilidade, PrioridadePublicacao.Normal,
			agendada ? DateTimeOffset.UtcNow.AddDays(1) : DateTimeOffset.UtcNow.AddMinutes(-1), null, "quem");

		if (arquivada)
		{
			publicacao.Arquivar();
		}

		var imagem = new ImagemNoTexto(publicacao.Id, "mural/imagens/a.bin", "chave");

		_imagens.Guardadas.Add(imagem);

		return (publicacao, imagem,
			new ObterImagemNoTextoHandler(new RepositorioFalso(new PublicacaoComSetor(publicacao, "Financeiro", "financeiro")), _imagens));
	}

	private static ObterImagemNoTextoQuery Consulta(
		Publicacao publicacao,
		ImagemNoTexto imagem,
		string[]? setoresDoLeitor = null,
		IReadOnlySet<string>? comEscrita = null,
		bool exigir = true) =>
		new(publicacao.Id, imagem.Id, setoresDoLeitor ?? [], comEscrita ?? SemEscrita, exigir);

	[Fact]
	public async Task HandleAsync_PublicacaoDaEmpresaNoAr_Entrega()
	{
		var (publicacao, imagem, handler) = Montar();

		var resultado = await handler.HandleAsync(Consulta(publicacao, imagem));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.CaminhoRelativo.Should().Be("mural/imagens/a.bin");
		resultado.Value.ChaveEmbrulhada.Should().Be("chave");
		resultado.Value.ContentType.Should().Be("image/webp");
		resultado.Value.ETag.Should().NotBeNullOrEmpty().And.NotContain("a.bin");
	}

	[Fact]
	public async Task HandleAsync_PublicacaoDoSetorParaLeitorDoSetor_Entrega()
	{
		var (publicacao, imagem, handler) = Montar(Visibilidade.Setor);

		var resultado = await handler.HandleAsync(Consulta(publicacao, imagem, ["FINANCEIRO"]));

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task HandleAsync_PublicacaoDoSetorParaOutroSetor_NotFound()
	{
		var (publicacao, imagem, handler) = Montar(Visibilidade.Setor);

		var resultado = await handler.HandleAsync(Consulta(publicacao, imagem, ["rh"]));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.NotFound);
	}

	[Fact]
	public async Task HandleAsync_AgendadaParaLeitor_NotFound()
	{
		var (publicacao, imagem, handler) = Montar(agendada: true);

		var resultado = await handler.HandleAsync(Consulta(publicacao, imagem, ["financeiro"]));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.NotFound);
	}

	[Fact]
	public async Task HandleAsync_AgendadaParaQuemEscreveNoSetor_EntregaComOutraCaixa()
	{
		var (publicacao, imagem, handler) = Montar(Visibilidade.Setor, agendada: true);

		var resultado = await handler.HandleAsync(
			Consulta(publicacao, imagem, comEscrita: new HashSet<string>(["FINANCEIRO"], StringComparer.Ordinal)));

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task HandleAsync_ModoAberto_DispensaVisibilidade()
	{
		var (publicacao, imagem, handler) = Montar(Visibilidade.Setor, agendada: true);

		var resultado = await handler.HandleAsync(Consulta(publicacao, imagem, exigir: false));

		resultado.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task HandleAsync_PublicacaoArquivada_NotFound()
	{
		var (publicacao, imagem, handler) = Montar(arquivada: true);

		var resultado = await handler.HandleAsync(Consulta(publicacao, imagem));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.NotFound);
	}

	[Fact]
	public async Task HandleAsync_ImagemDeOutraPublicacao_NotFound()
	{
		var (publicacao, _, handler) = Montar();
		var deOutra = new ImagemNoTexto(Guid.NewGuid(), "mural/imagens/b.bin", "chave-b");
		_imagens.Guardadas.Add(deOutra);

		var resultado = await handler.HandleAsync(Consulta(publicacao, deOutra));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.NotFound);
	}

	[Fact]
	public async Task HandleAsync_ImagemInexistente_NotFound()
	{
		var (publicacao, _, handler) = Montar();

		var resultado = await handler.HandleAsync(
			new ObterImagemNoTextoQuery(publicacao.Id, Guid.NewGuid(), [], SemEscrita, true));

		resultado.Error.Should().Be(IntranetErrors.Publicacoes.NotFound);
	}

	private sealed class RepositorioFalso(PublicacaoComSetor? encontrada) : IPublicacaoRepository
	{
		public Task AddAsync(Publicacao publicacao, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<PublicacaoComSetor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(encontrada);

		public Task<PagedResult<PublicacaoDto>> ListarNoMuralAsync(
			MuralCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Create<PublicacaoDto>([], criteria.Page, 0));

		public Task<IReadOnlyList<PublicacaoDto>> ListarDoSetorAsync(
			string setorSlug, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<PublicacaoDto>>([]);
	}
}
