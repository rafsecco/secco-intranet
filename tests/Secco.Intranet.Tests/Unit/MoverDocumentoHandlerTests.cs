using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Movimentos de documentos: subir, descer, recuar e desrecuar.</summary>
public class MoverDocumentoHandlerTests
{
	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class Cenario
	{
		public Cenario()
		{
			Setor = new Setor("Financeiro", "financeiro");
			Repositorio.RegistrarSetor(Setor);
			Handler = new MoverDocumentoHandler(Repositorio, Trilha);
		}

		public Setor Setor { get; }

		public DocumentoRepositorioEmMemoria Repositorio { get; } = new();

		public TrilhaFalsa Trilha { get; } = new();

		public MoverDocumentoHandler Handler { get; }

		public Documento Arquivo(string titulo, Guid? parentId = null, int ordem = 0, Setor? setor = null)
		{
			var documento = new Documento(
				DonoDeDocumento.DoSetor((setor ?? Setor).Id),
				titulo,
				null,
				"a.pdf",
				"application/pdf",
				10,
				Visibilidade.Setor,
				"t/ab/x",
				"secco-enc:v1:x",
				"alguem",
				parentId,
				ordem);
			Repositorio.Documentos.Add(documento);

			return documento;
		}

		public Documento Grupo(string titulo, int ordem = 0)
		{
			var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(Setor.Id), titulo, null, "alguem");
			grupo.Posicionar(null, ordem);
			Repositorio.Documentos.Add(grupo);

			return grupo;
		}

		public Task<Result> MoverAsync(Documento documento, MovimentoDeDocumento acao, string setor = "financeiro") =>
			Handler.HandleAsync(new MoverDocumentoCommand(setor, documento.Id, acao));

		public int OrdemDe(Documento documento) => documento.Ordem;

		public IEnumerable<string> Raiz() =>
			Repositorio.Documentos.Where(item => item.Ativo && item.ParentId is null)
				.OrderBy(item => item.Ordem).Select(item => item.Titulo);

		public IEnumerable<string> FilhosDe(Documento pai) =>
			Repositorio.Documentos.Where(item => item.Ativo && item.ParentId == pai.Id)
				.OrderBy(item => item.Ordem).Select(item => item.Titulo);

		public void NadaMudou(int gravacoesAntes)
		{
			Repositorio.Gravacoes.Should().Be(gravacoesAntes);
			Trilha.Registros.Should().BeEmpty();
		}
	}

	[Fact]
	public async Task Subir_TrocaComIrmaoDeCima()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);
		var b = cenario.Arquivo("B", ordem: 1);
		var c = cenario.Arquivo("C", ordem: 2);

		var resultado = await cenario.MoverAsync(c, MovimentoDeDocumento.Subir);

		resultado.IsSuccess.Should().BeTrue();
		cenario.Raiz().Should().Equal("A", "C", "B");
		cenario.OrdemDe(a).Should().Be(0);
		cenario.OrdemDe(c).Should().Be(1);
		cenario.OrdemDe(b).Should().Be(2);
		cenario.Repositorio.Gravacoes.Should().Be(1);
	}

	[Fact]
	public async Task Descer_TrocaComIrmaoDeBaixo()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);
		cenario.Arquivo("B", ordem: 1);
		cenario.Arquivo("C", ordem: 2);

		var resultado = await cenario.MoverAsync(a, MovimentoDeDocumento.Descer);

		resultado.IsSuccess.Should().BeTrue();
		cenario.Raiz().Should().Equal("B", "A", "C");
	}

	[Fact]
	public async Task Subir_Primeiro_Recusa()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);
		cenario.Arquivo("B", ordem: 1);

		var resultado = await cenario.MoverAsync(a, MovimentoDeDocumento.Subir);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		cenario.Raiz().Should().Equal("A", "B");
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Descer_Ultimo_Recusa()
	{
		var cenario = new Cenario();
		cenario.Arquivo("A", ordem: 0);
		var b = cenario.Arquivo("B", ordem: 1);

		var resultado = await cenario.MoverAsync(b, MovimentoDeDocumento.Descer);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		cenario.Raiz().Should().Equal("A", "B");
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Subir_FilhoPrimeiro_NaoSaiDoPai()
	{
		var cenario = new Cenario();
		cenario.Arquivo("Antes", ordem: 0);
		var pai = cenario.Arquivo("Pai", ordem: 1);
		var filho = cenario.Arquivo("Filho", pai.Id, 0);

		var resultado = await cenario.MoverAsync(filho, MovimentoDeDocumento.Subir);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		filho.ParentId.Should().Be(pai.Id);
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Recuar_ViraUltimoFilhoDoIrmaoDeCima()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);
		var filho = cenario.Arquivo("Filho", a.Id, 0);
		var b = cenario.Arquivo("B", ordem: 1);
		var c = cenario.Arquivo("C", ordem: 2);

		var resultado = await cenario.MoverAsync(b, MovimentoDeDocumento.Recuar);

		resultado.IsSuccess.Should().BeTrue();
		b.ParentId.Should().Be(a.Id);
		cenario.FilhosDe(a).Should().Equal("Filho", "B");
		cenario.OrdemDe(filho).Should().Be(0);
		cenario.OrdemDe(b).Should().Be(1);
		cenario.Raiz().Should().Equal("A", "C");
		cenario.OrdemDe(a).Should().Be(0);
		cenario.OrdemDe(c).Should().Be(1, "a raiz é renumerada sem buraco");
		cenario.Repositorio.Gravacoes.Should().Be(1);
	}

	[Fact]
	public async Task Recuar_Grupo_Recusa()
	{
		var cenario = new Cenario();
		cenario.Arquivo("A", ordem: 0);
		var grupo = cenario.Grupo("G", ordem: 1);

		var resultado = await cenario.MoverAsync(grupo, MovimentoDeDocumento.Recuar);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Recuar_ItemComFilhos_Recusa()
	{
		var cenario = new Cenario();
		cenario.Arquivo("A", ordem: 0);
		var b = cenario.Arquivo("B", ordem: 1);
		cenario.Arquivo("Filho", b.Id, 0);

		var resultado = await cenario.MoverAsync(b, MovimentoDeDocumento.Recuar);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		b.ParentId.Should().BeNull();
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Recuar_PrimeiroDaRaiz_Recusa()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);
		cenario.Arquivo("B", ordem: 1);

		var resultado = await cenario.MoverAsync(a, MovimentoDeDocumento.Recuar);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Recuar_JaEFilho_Recusa()
	{
		var cenario = new Cenario();
		var pai = cenario.Arquivo("Pai", ordem: 0);
		cenario.Arquivo("F0", pai.Id, 0);
		var f1 = cenario.Arquivo("F1", pai.Id, 1);

		var resultado = await cenario.MoverAsync(f1, MovimentoDeDocumento.Recuar);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		f1.ParentId.Should().Be(pai.Id);
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Desrecuar_VaiParaLogoDepoisDoPai()
	{
		var cenario = new Cenario();
		var pai = cenario.Arquivo("Pai", ordem: 0);
		var f0 = cenario.Arquivo("F0", pai.Id, 0);
		var f1 = cenario.Arquivo("F1", pai.Id, 1);
		var f2 = cenario.Arquivo("F2", pai.Id, 2);
		var depois = cenario.Arquivo("Depois", ordem: 1);

		var resultado = await cenario.MoverAsync(f1, MovimentoDeDocumento.Desrecuar);

		resultado.IsSuccess.Should().BeTrue();
		f1.ParentId.Should().BeNull();
		cenario.Raiz().Should().Equal("Pai", "F1", "Depois");
		cenario.OrdemDe(pai).Should().Be(0);
		cenario.OrdemDe(f1).Should().Be(1);
		cenario.OrdemDe(depois).Should().Be(2);
		cenario.FilhosDe(pai).Should().Equal("F0", "F2");
		cenario.OrdemDe(f0).Should().Be(0);
		cenario.OrdemDe(f2).Should().Be(1, "os antigos irmãos são renumerados");
		cenario.Repositorio.Gravacoes.Should().Be(1);
	}

	[Fact]
	public async Task Desrecuar_NaRaiz_Recusa()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);

		var resultado = await cenario.MoverAsync(a, MovimentoDeDocumento.Desrecuar);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Desrecuar_PaiForaDaRaizAtiva_Recusa()
	{
		// O arquivar recusa item com filhos; aqui o pai sai da raiz por fora, e o filho não pode
		// cair no topo em silêncio.
		var cenario = new Cenario();
		var pai = cenario.Arquivo("Pai", ordem: 0);
		var filho = cenario.Arquivo("F0", pai.Id, 0);
		cenario.Arquivo("Outro", ordem: 1);
		pai.Arquivar();

		var resultado = await cenario.MoverAsync(filho, MovimentoDeDocumento.Desrecuar);

		resultado.Error.Should().Be(IntranetErrors.Documentos.MovimentoInvalido);
		filho.ParentId.Should().Be(pai.Id);
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Mover_DocumentoDeOutroSetor_NaoEncontrado()
	{
		var cenario = new Cenario();
		var outro = new Setor("RH", "rh");
		cenario.Repositorio.RegistrarSetor(outro);
		var documento = cenario.Arquivo("A", setor: outro);
		cenario.Arquivo("B", setor: outro, ordem: 1);

		var resultado = await cenario.MoverAsync(documento, MovimentoDeDocumento.Descer);

		resultado.Error.Should().Be(IntranetErrors.Documentos.NotFound);
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Mover_Arquivado_NaoEncontrado()
	{
		var cenario = new Cenario();
		var a = cenario.Arquivo("A", ordem: 0);
		cenario.Arquivo("B", ordem: 1);
		a.Arquivar();

		var resultado = await cenario.MoverAsync(a, MovimentoDeDocumento.Descer);

		resultado.Error.Should().Be(IntranetErrors.Documentos.NotFound);
		cenario.NadaMudou(0);
	}

	[Fact]
	public async Task Mover_Inexistente_NaoEncontrado()
	{
		var cenario = new Cenario();

		var resultado = await cenario.Handler.HandleAsync(
			new MoverDocumentoCommand("financeiro", Guid.NewGuid(), MovimentoDeDocumento.Subir));

		resultado.Error.Should().Be(IntranetErrors.Documentos.NotFound);
	}

	[Fact]
	public async Task Mover_Audita()
	{
		var cenario = new Cenario();
		var pai = cenario.Arquivo("Pai", ordem: 0);
		var filho = cenario.Arquivo("Filho", pai.Id, 0);
		var b = cenario.Arquivo("B", ordem: 1);

		await cenario.MoverAsync(b, MovimentoDeDocumento.Recuar);
		await cenario.MoverAsync(filho, MovimentoDeDocumento.Desrecuar);

		cenario.Trilha.Registros.Should().HaveCount(2);

		var recuar = cenario.Trilha.Registros[0];
		recuar.Verbo.Should().Be(VerbosDeAuditoria.DocumentoMover);
		recuar.Verbo.Should().Be("documento.mover");
		recuar.Recurso.Should().Be(RecursosDeAuditoria.Documento);
		recuar.RecursoId.Should().Be(b.Id.ToString());
		var payload = JsonDocument.Parse(recuar.Metadata!).RootElement;
		payload.GetProperty("setor").GetString().Should().Be("financeiro");
		payload.GetProperty("documento").GetString().Should().Be(b.Id.ToString());
		payload.GetProperty("acao").GetString().Should().Be("recuar");
		payload.GetProperty("paiDe").ValueKind.Should().Be(JsonValueKind.Null);
		payload.GetProperty("paiPara").GetString().Should().Be(pai.Id.ToString());
		payload.GetProperty("de").GetInt32().Should().Be(1);
		payload.GetProperty("para").GetInt32().Should().Be(1);

		var desrecuar = JsonDocument.Parse(cenario.Trilha.Registros[1].Metadata!).RootElement;
		desrecuar.GetProperty("acao").GetString().Should().Be("desrecuar");
		desrecuar.GetProperty("paiDe").GetString().Should().Be(pai.Id.ToString());
		desrecuar.GetProperty("paiPara").ValueKind.Should().Be(JsonValueKind.Null);
		desrecuar.GetProperty("de").GetInt32().Should().Be(0);
		desrecuar.GetProperty("para").GetInt32().Should().Be(1);
	}
}
