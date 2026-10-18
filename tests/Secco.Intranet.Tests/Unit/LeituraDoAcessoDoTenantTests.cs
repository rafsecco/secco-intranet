using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Leituras do acesso do tenant administrado: tenant explícito, reservados fora dos seletores, paginação segura.</summary>
public class LeituraDoAcessoDoTenantTests
{
	private static readonly Guid Tenant = Guid.NewGuid();
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bia = Guid.NewGuid();

	private static GestaoDeAcessoDoTenantFalsa Gestao() => new GestaoDeAcessoDoTenantFalsa()
		.ComPerfil("compras-aprovador", false, "pedido:aprovar")
		.ComPerfil("installation-operator", reservado: true)
		.ComUsuario(Ana, "ana@compras.com", SituacaoDoUsuario.Ativo, perfis: "compras-aprovador")
		.ComUsuario(Bia, "bia@compras.com", SituacaoDoUsuario.Ativo);

	[Fact]
	public async Task PerfisAtribuiveis_SemReservados()
	{
		var atribuiveis = await new LeituraDoAcessoDoTenantHandler(Gestao()).PerfisAtribuiveisAsync(Tenant);

		atribuiveis.Value.Should().Equal("compras-aprovador");
	}

	[Fact]
	public async Task ObterPerfil_CandidatosSaoAtivosSemOPerfil()
	{
		var tela = await new LeituraDoAcessoDoTenantHandler(Gestao()).ObterPerfilAsync(Tenant, "compras-aprovador", 1);

		tela.Value.Candidatos.Select(u => u.Id).Should().Equal(Bia);
		tela.Value.Membros.Itens.Select(m => m.UsuarioId).Should().Equal(Ana);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public async Task ObterPerfil_PaginaNaoPositiva_ViraPrimeira(int pagina)
	{
		var gestao = Gestao();

		await new LeituraDoAcessoDoTenantHandler(gestao).ObterPerfilAsync(Tenant, "compras-aprovador", pagina);

		gestao.Chamadas.Should().Contain($"listarMembros:{Tenant}:compras-aprovador:1");
	}

	[Fact]
	public async Task ListarUsuarios_BuscaPorEmailEPagina999SemErro()
	{
		var leitura = new LeituraDoAcessoDoTenantHandler(Gestao());

		(await leitura.ListarUsuariosAsync(Tenant, "BIA", 1)).Value.Items.Select(u => u.Id).Should().Equal(Bia);
		(await leitura.ListarUsuariosAsync(Tenant, null, 999)).Value.Items.Should().BeEmpty();
	}

	[Fact]
	public async Task ObterUsuario_AtribuiveisExcluemOsQueJaTemEReservados()
	{
		var tela = await new LeituraDoAcessoDoTenantHandler(Gestao()).ObterUsuarioAsync(Tenant, Bia);

		tela.Value.PerfisAtribuiveis.Should().Equal("compras-aprovador");
	}

	[Fact]
	public async Task TodaLeitura_UsaOTenantRecebido()
	{
		var gestao = Gestao();
		var leitura = new LeituraDoAcessoDoTenantHandler(gestao);

		await leitura.ListarPerfisAsync(Tenant);
		await leitura.ObterUsuarioAsync(Tenant, Ana);
		await leitura.ObterFederacaoAsync(Tenant);

		gestao.Chamadas.Should().OnlyContain(c => c.Contains(Tenant.ToString(), StringComparison.Ordinal));
	}

	[Fact]
	public async Task ObterPerfil_TrazOCatalogoDaPlataforma_EAsForaDoCatalogo()
	{
		var gestao = Gestao();
		gestao.Catalogo = [new ProdutoDoCatalogoDto("compras", [new PermissaoDoCatalogoDto("pedido:aprovar", "Aprovar pedidos")])];
		gestao.ComPerfil("legado", false, "pedido:aprovar", "pedido:antigo");

		var tela = await new LeituraDoAcessoDoTenantHandler(gestao).ObterPerfilAsync(Tenant, "legado", 1);

		tela.Value.Catalogo.Should().BeEquivalentTo(gestao.Catalogo);
		tela.Value.Perfil.PermissoesForaDoCatalogo.Should().Equal("pedido:antigo");
	}
}
