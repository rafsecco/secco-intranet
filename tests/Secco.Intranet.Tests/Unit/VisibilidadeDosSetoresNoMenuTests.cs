using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Web.Navigation;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class VisibilidadeDosSetoresNoMenuTests
{
	private sealed class PermissoesFalsas : IPermissoesDeSetor
	{
		public HashSet<string> Leitura { get; init; } = [];

		public HashSet<string> Escrita { get; init; } = [];

		public bool FalharNaEscrita { get; init; }

		public bool FalharNaLeitura { get; init; }

		public List<(string Acao, IReadOnlyList<string> Slugs)> Consultas { get; } = [];

		public Task<IReadOnlySet<string>> SlugsComPermissaoAsync(
			ClaimsPrincipal usuario, string acao, IReadOnlyList<string> setoresDoTenant, CancellationToken cancellationToken = default)
		{
			Consultas.Add((acao, setoresDoTenant));

			if ((acao == "write" && FalharNaEscrita) || (acao == "read" && FalharNaLeitura))
			{
				throw new InvalidOperationException("falha simulada");
			}

			var concedidos = acao == "write" ? Escrita : Leitura;

			return Task.FromResult<IReadOnlySet<string>>(setoresDoTenant.Where(concedidos.Contains).ToHashSet());
		}
	}

	private static SetorDto Setor(string slug) =>
		new(Guid.NewGuid(), slug, slug, Domain.Setores.Setor.IconePadrao, Fixo: false, Ativo: true, CreatedAt: DateTimeOffset.UtcNow);

	private static IReadOnlyList<ItemMenuDto> Arvore(NivelDeAcessoItemMenu nivel) =>
	[
		new(Guid.NewGuid(), Guid.NewGuid(), "Item", "item", TipoDeItemMenu.Personalizado, "/item", null, 0, true, nivel),
	];

	private static Task<VisibilidadeDosSetores> Resolver(
		PermissoesFalsas permissoes, IReadOnlyList<SetorDto> ativos, Dictionary<Guid, IReadOnlyList<ItemMenuDto>>? arvores, bool modoAberto = false) =>
		VisibilidadeDosSetoresNoMenu.ResolverAsync(
			permissoes, new ClaimsPrincipal(), ativos, arvores, modoAberto, NullLogger.Instance, CancellationToken.None);

	[Fact]
	public async Task SemItemRestrito_NaoConsultaAEscrita()
	{
		var setor = Setor("financeiro");
		var permissoes = new PermissoesFalsas { Leitura = ["financeiro"] };

		var visibilidade = await Resolver(permissoes, [setor], new() { [setor.Id] = Arvore(NivelDeAcessoItemMenu.Leitura) });

		visibilidade.Setores.Should().Equal(setor);
		permissoes.Consultas.Should().NotContain(consulta => consulta.Acao == "write");
	}

	[Fact]
	public async Task ConsultaAEscrita_SoNosSetoresComItemRestrito()
	{
		var comum = Setor("comum");
		var restrito = Setor("restrito");
		var permissoes = new PermissoesFalsas { Leitura = ["comum", "restrito"], Escrita = ["restrito"] };

		var visibilidade = await Resolver(permissoes, [comum, restrito], new()
		{
			[comum.Id] = Arvore(NivelDeAcessoItemMenu.Leitura),
			[restrito.Id] = Arvore(NivelDeAcessoItemMenu.Escrita),
		});

		permissoes.Consultas.Single(consulta => consulta.Acao == "write").Slugs.Should().Equal("restrito");
		visibilidade.ComEscrita.Should().BeEquivalentTo([restrito.Id]);
		visibilidade.SemLeitura.Should().BeEmpty();
	}

	[Fact]
	public async Task SoComEscrita_SetorComItemRestrito_EntraSemLeitura()
	{
		var setor = Setor("financeiro");
		var permissoes = new PermissoesFalsas { Escrita = ["financeiro"] };

		var visibilidade = await Resolver(permissoes, [setor], new() { [setor.Id] = Arvore(NivelDeAcessoItemMenu.Escrita) });

		visibilidade.Setores.Should().Equal(setor);
		visibilidade.ComEscrita.Should().BeEquivalentTo([setor.Id]);
		visibilidade.SemLeitura.Should().BeEquivalentTo([setor.Id]);
	}

	[Fact]
	public async Task SoComEscrita_SetorSemItemRestrito_ContinuaDeFora()
	{
		var setor = Setor("financeiro");
		var permissoes = new PermissoesFalsas { Escrita = ["financeiro"] };

		var visibilidade = await Resolver(permissoes, [setor], new() { [setor.Id] = Arvore(NivelDeAcessoItemMenu.Leitura) });

		visibilidade.Setores.Should().BeEmpty();
	}

	[Fact]
	public async Task ModoAberto_LiberaTudo_SemConsultarPermissao()
	{
		var setor = Setor("financeiro");
		var permissoes = new PermissoesFalsas();

		var visibilidade = await Resolver(
			permissoes, [setor], new() { [setor.Id] = Arvore(NivelDeAcessoItemMenu.Escrita) }, modoAberto: true);

		visibilidade.Setores.Should().Equal(setor);
		visibilidade.ComEscrita.Should().BeEquivalentTo([setor.Id]);
		visibilidade.SemLeitura.Should().BeEmpty();
		permissoes.Consultas.Should().BeEmpty();
	}

	[Fact]
	public async Task FalhaNaEscrita_EscondeSoORestrito()
	{
		var setor = Setor("financeiro");
		var permissoes = new PermissoesFalsas { Leitura = ["financeiro"], FalharNaEscrita = true };

		var visibilidade = await Resolver(permissoes, [setor], new() { [setor.Id] = Arvore(NivelDeAcessoItemMenu.Escrita) });

		visibilidade.Setores.Should().Equal(setor);
		visibilidade.ComEscrita.Should().BeEmpty();
	}

	[Fact]
	public async Task FalhaNaLeitura_DeixaOMenuSemSetores()
	{
		var setor = Setor("financeiro");
		var permissoes = new PermissoesFalsas { FalharNaLeitura = true };

		var visibilidade = await Resolver(permissoes, [setor], new() { [setor.Id] = Arvore(NivelDeAcessoItemMenu.Leitura) });

		visibilidade.Setores.Should().BeEmpty();
		visibilidade.ComEscrita.Should().BeEmpty();
	}

	[Fact]
	public async Task SemArvores_SoALeituraDecide()
	{
		var setor = Setor("financeiro");
		var permissoes = new PermissoesFalsas { Leitura = ["financeiro"] };

		var visibilidade = await Resolver(permissoes, [setor], arvores: null);

		visibilidade.Setores.Should().Equal(setor);
		permissoes.Consultas.Should().NotContain(consulta => consulta.Acao == "write");
	}
}
