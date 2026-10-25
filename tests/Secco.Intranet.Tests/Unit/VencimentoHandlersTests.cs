using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Vencimentos;
using Secco.Intranet.Domain.Vencimentos;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Casos de uso de vencimento: regras de borda, setor da rota, histórico e trilha.</summary>
public class VencimentoHandlersTests
{
	private const string Slug = "ti";

	// 2026-10-24 12:00Z = 09:00 em São Paulo, mesmo dia.
	private static readonly DateOnly Hoje = new(2026, 10, 24);
	private static readonly Guid Setor = Guid.NewGuid();

	private sealed class TrilhaFalsa : ITrilhaDeAuditoria
	{
		public List<RegistroDeAuditoria> Registros { get; } = [];

		public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default)
		{
			Registros.Add(registro);

			return Task.CompletedTask;
		}
	}

	private sealed class AtorFalso(string? nome) : IAtorAtual
	{
		public AtorDaAcao? Atual() => nome is null ? null : new AtorDaAcao("u1", nome);
	}

	private sealed class DiretorioFalso(params UsuarioDoTenant[] usuarios) : IDiretorioDeUsuarios
	{
		public Task<IReadOnlyList<UsuarioDoTenant>> ListarDoTenantAtualAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<UsuarioDoTenant>>(usuarios);
	}

	private sealed class RepositorioFalso : IItemVencimentoRepository
	{
		public List<ItemVencimento> Itens { get; } = [];

		public List<RenovacaoDeVencimento> Renovacoes { get; } = [];

		public List<AvisoDeVencimentoEnviado> Avisos { get; } = [];

		public int Salvou { get; private set; }

		public Task AddAsync(ItemVencimento item, CancellationToken ct = default)
		{
			Itens.Add(item);

			return Task.CompletedTask;
		}

		public Task<ItemVencimento?> GetParaEdicaoAsync(Guid id, CancellationToken ct = default) =>
			Task.FromResult(Itens.FirstOrDefault(i => i.Id == id));

		public Task<IReadOnlyList<ItemVencimento>> ListarDoSetorAsync(Guid setorId, bool incluirEncerrados, CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyList<ItemVencimento>>(Itens
				.Where(i => i.SetorId == setorId && (incluirEncerrados || !i.EstaEncerrado))
				.OrderBy(i => i.DataVencimento)
				.ToList());

		public Task<IReadOnlyList<RenovacaoDeVencimento>> ListarRenovacoesAsync(Guid itemId, CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyList<RenovacaoDeVencimento>>(Renovacoes.Where(r => r.ItemVencimentoId == itemId).ToList());

		public Task<IReadOnlyList<AvisoDeVencimentoEnviado>> ListarAvisosAsync(Guid itemId, CancellationToken ct = default) =>
			Task.FromResult<IReadOnlyList<AvisoDeVencimentoEnviado>>(Avisos.Where(a => a.ItemVencimentoId == itemId).ToList());

		public Task<IReadOnlyList<ItemParaAviso>> ListarAtivosParaAvisoAsync(CancellationToken ct = default) =>
			throw new NotSupportedException();

		public Task<bool> TentarRegistrarAvisoAsync(AvisoDeVencimentoEnviado aviso, CancellationToken ct = default) =>
			throw new NotSupportedException();

		public Task RemoverAvisoAsync(Guid avisoId, CancellationToken ct = default) => throw new NotSupportedException();

		public Task AdicionarRenovacaoAsync(RenovacaoDeVencimento renovacao, CancellationToken ct = default)
		{
			Renovacoes.Add(renovacao);

			return Task.CompletedTask;
		}

		public Task SaveChangesAsync(CancellationToken ct = default)
		{
			Salvou++;

			return Task.CompletedTask;
		}
	}

	private sealed class Cenario
	{
		public RepositorioFalso Repositorio { get; } = new();

		public TrilhaFalsa Trilha { get; } = new();

		public RelogioDeVencimentos Relogio { get; } = new(
			new FakeTimeProvider(DateTimeOffset.Parse("2026-10-24T12:00:00Z")), new VencimentosOptions());

		public IDiretorioDeUsuarios Diretorio { get; init; } = new DiretorioFalso();

		public IAtorAtual Ator { get; init; } = new AtorFalso("Ana");

		public CriarVencimentoHandler Criar => new(Repositorio, Diretorio, new IntranetOptions(), Trilha);

		public EditarVencimentoHandler Editar => new(Repositorio, Diretorio, new IntranetOptions(), Trilha);

		public RenovarVencimentoHandler Renovar => new(Repositorio, Relogio, new IntranetOptions(), Ator, Trilha);

		public EncerrarVencimentoHandler Encerrar => new(Repositorio, Relogio, Ator, Trilha);

		public ReativarVencimentoHandler Reativar => new(Repositorio, Relogio, Trilha);

		public ListarVencimentosDoSetorHandler Listar => new(Repositorio, Relogio);

		public ObterVencimentoHandler Obter => new(Repositorio, Relogio);

		public ItemVencimento Com(string titulo, DateOnly data, Guid? setor = null)
		{
			var item = new ItemVencimento(setor ?? Setor, titulo, null, data, null, null);
			Repositorio.Itens.Add(item);

			return item;
		}
	}

	private static CriarVencimentoCommand NovoComando(Guid? responsavel = null) =>
		new(Setor, Slug, "Certificado digital", "A1", new DateOnly(2026, 12, 31), responsavel, null);

	[Fact]
	public async Task Criar_RegistraNaTrilha()
	{
		var c = new Cenario();

		var resultado = await c.Criar.HandleAsync(NovoComando());

		resultado.IsSuccess.Should().BeTrue();
		var item = c.Repositorio.Itens.Should().ContainSingle().Subject;
		item.Id.Should().Be(resultado.Value);
		item.Antecedencias.Should().Equal(30, 7, 1);
		c.Repositorio.Salvou.Should().Be(1);

		var registro = c.Trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be("vencimento.criar");
		registro.Recurso.Should().Be("vencimento");
		registro.RecursoId.Should().Be(item.Id.ToString());
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("titulo").GetString().Should().Be("Certificado digital");
		json.RootElement.GetProperty("setor").GetString().Should().Be(Slug);
		json.RootElement.GetProperty("dataVencimento").GetString().Should().Be("2026-12-31");
	}

	[Fact]
	public async Task Criar_ResponsavelSemRoleDoSetor_Recusa()
	{
		var responsavel = Guid.NewGuid();
		var c = new Cenario
		{
			Diretorio = new DiretorioFalso(new UsuarioDoTenant(responsavel, null, ["rh-user"])),
		};

		var resultado = await c.Criar.HandleAsync(NovoComando(responsavel));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.ResponsavelSemAcesso);
		c.Repositorio.Itens.Should().BeEmpty();
		c.Repositorio.Salvou.Should().Be(0);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_DiretorioVazio_RecusaResponsavel()
	{
		var c = new Cenario();

		var resultado = await c.Criar.HandleAsync(NovoComando(Guid.NewGuid()));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.ResponsavelSemAcesso);
	}

	[Fact]
	public async Task Criar_ResponsavelComRoleDoSetor_Aceita()
	{
		var responsavel = Guid.NewGuid();
		var c = new Cenario
		{
			Diretorio = new DiretorioFalso(new UsuarioDoTenant(responsavel, null, ["TI-User"])),
		};

		var resultado = await c.Criar.HandleAsync(NovoComando(responsavel));

		resultado.IsSuccess.Should().BeTrue();
		c.Repositorio.Itens.Single().ResponsavelId.Should().Be(responsavel);
	}

	[Fact]
	public async Task Criar_AntecedenciasInvalidas_DevolveErroSemPersistir()
	{
		var c = new Cenario();

		var resultado = await c.Criar.HandleAsync(NovoComando() with { Antecedencias = [0] });

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.AntecedenciasInvalidas);
		c.Repositorio.Itens.Should().BeEmpty();
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_TituloVazio_DevolveErro()
	{
		var c = new Cenario();

		var resultado = await c.Criar.HandleAsync(NovoComando() with { Titulo = "  " });

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.TituloRequired);
	}

	[Fact]
	public async Task Editar_ItemDeOutroSetor_NotFound()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31), Guid.NewGuid());

		var resultado = await c.Editar.HandleAsync(
			new EditarVencimentoCommand(item.Id, Setor, Slug, "Outro", null, new DateOnly(2027, 1, 1), null, [7]));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.NotFound);
		item.Titulo.Should().Be("Contrato");
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_ItemDeOutroSetor_ComTituloVazio_NotFound()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31), Guid.NewGuid());

		var resultado = await c.Editar.HandleAsync(
			new EditarVencimentoCommand(item.Id, Setor, Slug, "  ", null, new DateOnly(2027, 1, 1), null, [7]));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.NotFound);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_MudandoData_RegistraDatasNaTrilha()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));

		var resultado = await c.Editar.HandleAsync(
			new EditarVencimentoCommand(item.Id, Setor, Slug, "Contrato", null, new DateOnly(2027, 1, 15), null, [30, 7]));

		resultado.IsSuccess.Should().BeTrue();
		item.Ciclo.Should().Be(2);
		item.Antecedencias.Should().Equal(30, 7);
		using var json = JsonDocument.Parse(c.Trilha.Registros.Single().Metadata!);
		json.RootElement.GetProperty("dataAnterior").GetString().Should().Be("2026-12-31");
		json.RootElement.GetProperty("dataNova").GetString().Should().Be("2027-01-15");
		c.Trilha.Registros.Single().Verbo.Should().Be("vencimento.editar");
	}

	[Fact]
	public async Task Editar_ItemEncerrado_DevolveEncerrado()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));
		item.Encerrar("Ana", DateTimeOffset.UtcNow);

		var resultado = await c.Editar.HandleAsync(
			new EditarVencimentoCommand(item.Id, Setor, Slug, "Novo", null, item.DataVencimento, null, [7]));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.Encerrado);
		item.Titulo.Should().Be("Contrato");
	}

	[Fact]
	public async Task Renovar_DataInvalida_DevolveErroDeValidacao()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));

		var resultado = await c.Renovar.HandleAsync(
			new RenovarVencimentoCommand(item.Id, Setor, Slug, new DateOnly(2026, 12, 31), null));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.DataDeRenovacaoInvalida);
		item.DataVencimento.Should().Be(new DateOnly(2026, 12, 31));
		c.Repositorio.Renovacoes.Should().BeEmpty();
		c.Repositorio.Salvou.Should().Be(0);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Renovar_GravaHistoricoERegistraNaTrilha()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));

		var resultado = await c.Renovar.HandleAsync(
			new RenovarVencimentoCommand(item.Id, Setor, Slug, new DateOnly(2027, 12, 31), "Renovado por 1 ano"));

		resultado.IsSuccess.Should().BeTrue();
		item.DataVencimento.Should().Be(new DateOnly(2027, 12, 31));
		item.Ciclo.Should().Be(2);

		var renovacao = c.Repositorio.Renovacoes.Should().ContainSingle().Subject;
		renovacao.DataAnterior.Should().Be(new DateOnly(2026, 12, 31));
		renovacao.DataNova.Should().Be(new DateOnly(2027, 12, 31));
		renovacao.RenovadoPor.Should().Be("Ana");
		renovacao.Observacao.Should().Be("Renovado por 1 ano");

		var registro = c.Trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be("vencimento.renovar");
		using var json = JsonDocument.Parse(registro.Metadata!);
		json.RootElement.GetProperty("dataAnterior").GetString().Should().Be("2026-12-31");
		json.RootElement.GetProperty("dataNova").GetString().Should().Be("2027-12-31");
	}

	[Fact]
	public async Task Renovar_SemAtor_UsaSistema()
	{
		var c = new Cenario { Ator = new AtorFalso(null) };
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));

		await c.Renovar.HandleAsync(new RenovarVencimentoCommand(item.Id, Setor, Slug, new DateOnly(2027, 12, 31), null));

		c.Repositorio.Renovacoes.Single().RenovadoPor.Should().Be("sistema");
	}

	[Fact]
	public async Task Encerrar_Repetido_NaoRegistraDuasVezes()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));
		var comando = new EncerrarVencimentoCommand(item.Id, Setor, Slug);

		var primeiro = await c.Encerrar.HandleAsync(comando);
		var segundo = await c.Encerrar.HandleAsync(comando);

		primeiro.IsSuccess.Should().BeTrue();
		segundo.IsSuccess.Should().BeTrue();
		item.EstaEncerrado.Should().BeTrue();
		item.EncerradoPor.Should().Be("Ana");
		item.EncerradoEm.Should().Be(DateTimeOffset.Parse("2026-10-24T12:00:00Z"));
		c.Repositorio.Salvou.Should().Be(1);
		c.Trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be("vencimento.encerrar");
	}

	[Fact]
	public async Task Encerrar_ItemDeOutroSetor_NotFound()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31), Guid.NewGuid());

		var resultado = await c.Encerrar.HandleAsync(new EncerrarVencimentoCommand(item.Id, Setor, Slug));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.NotFound);
		item.EstaEncerrado.Should().BeFalse();
	}

	[Fact]
	public async Task Reativar_ItemEncerrado_AbreCicloERegistra()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));
		item.Encerrar("Ana", DateTimeOffset.UtcNow);

		var resultado = await c.Reativar.HandleAsync(new ReativarVencimentoCommand(item.Id, Setor, Slug, new DateOnly(2027, 3, 1)));

		resultado.IsSuccess.Should().BeTrue();
		item.EstaEncerrado.Should().BeFalse();
		item.DataVencimento.Should().Be(new DateOnly(2027, 3, 1));
		item.Ciclo.Should().Be(2);
		c.Trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be("vencimento.reativar");
	}

	[Fact]
	public async Task Reativar_ItemAtivo_DevolveNaoEncerrado()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));

		var resultado = await c.Reativar.HandleAsync(new ReativarVencimentoCommand(item.Id, Setor, Slug, new DateOnly(2027, 3, 1)));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.NaoEncerrado);
		item.Ciclo.Should().Be(1);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Reativar_DataNoPassado_DevolveErroSemPersistir()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));
		item.Encerrar("Ana", DateTimeOffset.UtcNow);

		var resultado = await c.Reativar.HandleAsync(new ReativarVencimentoCommand(item.Id, Setor, Slug, Hoje));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.DataDeRenovacaoInvalida);
		item.EstaEncerrado.Should().BeTrue();
		c.Repositorio.Salvou.Should().Be(0);
	}

	[Fact]
	public async Task Listar_OrdenaPorData_ESemEncerradosPorPadrao()
	{
		var c = new Cenario();
		c.Com("Tardio", new DateOnly(2027, 5, 1));
		c.Com("Cedo", new DateOnly(2026, 10, 30));
		c.Com("Encerrado", new DateOnly(2026, 11, 1)).Encerrar("Ana", DateTimeOffset.UtcNow);
		c.Com("Outro setor", new DateOnly(2026, 10, 25), Guid.NewGuid());

		var ativos = await c.Listar.HandleAsync(Setor, false);
		var todos = await c.Listar.HandleAsync(Setor, true);

		ativos.Value.Select(i => i.Titulo).Should().Equal("Cedo", "Tardio");
		ativos.Value[0].Estado.Should().Be(EstadoDoVencimento.AVencer);
		ativos.Value[1].Estado.Should().Be(EstadoDoVencimento.EmDia);
		todos.Value.Select(i => i.Titulo).Should().Equal("Cedo", "Encerrado", "Tardio");
		todos.Value[1].Estado.Should().Be(EstadoDoVencimento.Encerrado);
	}

	[Fact]
	public async Task Obter_CalculaEstadoEProximosAvisos()
	{
		var c = new Cenario();
		// 2026-11-01 com 30/7/1: marcos em 10-02 (passado), 10-25, 10-31 e 11-01; hoje é 10-24.
		var item = c.Com("Contrato", new DateOnly(2026, 11, 1));
		c.Repositorio.Avisos.Add(new AvisoDeVencimentoEnviado(item.Id, 1, 1, ResultadoDoAviso.Enviado, 1, DateTimeOffset.UtcNow));
		c.Repositorio.Avisos.Add(new AvisoDeVencimentoEnviado(item.Id, 0, 7, ResultadoDoAviso.Enviado, 1, DateTimeOffset.UtcNow));

		var resultado = await c.Obter.HandleAsync(item.Id, Setor);

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Estado.Should().Be(EstadoDoVencimento.AVencer);
		resultado.Value.Ciclo.Should().Be(1);
		resultado.Value.Antecedencias.Should().Equal(30, 7, 1);
		// O marco 1 foi tratado no ciclo atual; o marco 7 só foi tratado no ciclo 0, então ainda vale.
		resultado.Value.ProximosAvisos.Should().Equal((7, new DateOnly(2026, 10, 25)), (0, new DateOnly(2026, 11, 1)));
		resultado.Value.Avisos.Should().HaveCount(2);
	}

	[Fact]
	public async Task Obter_ItemDeOutroSetor_NotFound()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31), Guid.NewGuid());

		var resultado = await c.Obter.HandleAsync(item.Id, Setor);

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.NotFound);
	}

	[Fact]
	public async Task Criar_DescricaoAcimaDoLimite_Recusa_SemGravar()
	{
		var c = new Cenario();
		var limite = new IntranetOptions().MaxDescriptionLength;

		var resultado = await c.Criar.HandleAsync(NovoComando() with { Descricao = new string('x', limite + 1) });

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.DescricaoTooLong(limite));
		c.Repositorio.Itens.Should().BeEmpty();
		c.Repositorio.Salvou.Should().Be(0);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_DescricaoAcimaDoLimite_Recusa_SemGravar()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));
		var limite = new IntranetOptions().MaxDescriptionLength;

		var resultado = await c.Editar.HandleAsync(
			new EditarVencimentoCommand(item.Id, Setor, Slug, "Contrato", new string('x', limite + 1), item.DataVencimento, null, [7]));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.DescricaoTooLong(limite));
		item.Descricao.Should().BeNull();
		c.Repositorio.Salvou.Should().Be(0);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Renovar_ObservacaoAcimaDoLimite_Recusa_SemGravar()
	{
		var c = new Cenario();
		var item = c.Com("Contrato", new DateOnly(2026, 12, 31));
		var limite = new IntranetOptions().MaxDescriptionLength;

		var resultado = await c.Renovar.HandleAsync(
			new RenovarVencimentoCommand(item.Id, Setor, Slug, new DateOnly(2027, 12, 31), new string('x', limite + 1)));

		resultado.Error.Should().Be(IntranetErrors.Vencimentos.ObservacaoTooLong(limite));
		item.DataVencimento.Should().Be(new DateOnly(2026, 12, 31));
		c.Repositorio.Renovacoes.Should().BeEmpty();
		c.Repositorio.Salvou.Should().Be(0);
		c.Trilha.Registros.Should().BeEmpty();
	}
}
