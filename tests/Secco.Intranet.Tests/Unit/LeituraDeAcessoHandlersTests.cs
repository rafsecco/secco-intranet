using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeituraDeAcessoHandlersTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();

	[Fact]
	public async Task ListarPerfis_OrdenaPorNome_EApontaOsPerfisDoProdutoQueFaltam()
	{
		var gestao = new GestaoDeAcessoFalsa().ComPerfil("rh-user").ComPerfil("intranet-admin").ComPerfil("a-comum");

		var resultado = await new ListarPerfisHandler(gestao).HandleAsync();

		resultado.Value.Perfis.Select(p => p.Nome).Should().Equal("a-comum", "intranet-admin", "rh-user");
		resultado.Value.PerfisDoProdutoFaltando.Should().Equal("inventario-admin");
	}

	[Fact]
	public async Task ListarPerfis_ProdutoFaltandoIgnoraCaixa()
	{
		var gestao = new GestaoDeAcessoFalsa().ComPerfil("Intranet-Admin").ComPerfil("INVENTARIO-ADMIN");

		var resultado = await new ListarPerfisHandler(gestao).HandleAsync();

		resultado.Value.PerfisDoProdutoFaltando.Should().BeEmpty();
	}

	[Fact]
	public async Task ObterPerfil_TrazMembrosECandidatosAtivosSemOPerfil()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComPerfil("gerente-de-compras")
			.ComUsuario(Ana, "ana@x.com", SituacaoDoUsuario.Ativo, "gerente-de-compras")
			.ComUsuario(Bruno, "bruno@x.com", SituacaoDoUsuario.Ativo)
			.ComUsuario(Carla, "carla@x.com", SituacaoDoUsuario.Desativado);

		var resultado = await new ObterPerfilHandler(gestao).HandleAsync(new ObterPerfilQuery("gerente-de-compras", 1));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Membros.Itens.Select(m => m.Email).Should().Equal("ana@x.com");
		resultado.Value.Candidatos.Select(c => c.Email).Should().Equal("bruno@x.com");
	}

	[Fact]
	public async Task ObterPerfil_Reservado_NaoOfereceCandidatos()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComPerfil("installation-operator", reservado: true)
			.ComUsuario(Bruno, "bruno@x.com");

		var resultado = await new ObterPerfilHandler(gestao).HandleAsync(new ObterPerfilQuery("installation-operator", 1));

		resultado.Value.Candidatos.Should().BeEmpty();
	}

	[Fact]
	public async Task ObterPerfil_Inexistente_DevolveNotFound()
	{
		var resultado = await new ObterPerfilHandler(new GestaoDeAcessoFalsa()).HandleAsync(new ObterPerfilQuery("nao-existe", 1));

		resultado.Error.Should().Be(IntranetErrors.Acesso.PerfilNaoEncontrado);
	}

	[Fact]
	public async Task ListarUsuarios_BuscaPorEmailSemDiferenciarCaixa_EOrdena()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComUsuario(Bruno, "bruno@x.com")
			.ComUsuario(Ana, "ANA@x.com")
			.ComUsuario(Carla, "carla@y.com");

		var resultado = await new ListarUsuariosHandler(gestao).HandleAsync(new ListarUsuariosQuery("@x", 1));

		resultado.Value.Items.Select(u => u.Email).Should().Equal("ANA@x.com", "bruno@x.com");
		resultado.Value.TotalCount.Should().Be(2);
	}

	[Fact]
	public async Task ListarUsuarios_PaginaAlemDoFim_DevolveListaVaziaSemQuebrar()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, "ana@x.com");

		var resultado = await new ListarUsuariosHandler(gestao).HandleAsync(new ListarUsuariosQuery(null, 99));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Items.Should().BeEmpty();
		resultado.Value.TotalCount.Should().Be(1);
	}

	[Fact]
	public async Task ListarUsuarios_UsuarioSemEmail_NaoQuebraABusca()
	{
		var gestao = new GestaoDeAcessoFalsa().ComUsuario(Ana, string.Empty).ComUsuario(Bruno, "bruno@x.com");

		var resultado = await new ListarUsuariosHandler(gestao).HandleAsync(new ListarUsuariosQuery("bruno", 1));

		resultado.Value.Items.Should().ContainSingle();
	}

	[Fact]
	public async Task ObterUsuario_AgrupaOsPerfisPorSetor_EOfereceSoOQueAindaNaoTem()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComPerfil("marketing-admin").ComPerfil("marketing-user").ComPerfil("rh-user").ComPerfil("ti-admin")
			.ComPerfil("gerente-de-compras").ComPerfil("all-users").ComPerfil("installation-operator", reservado: true)
			.ComUsuario(Ana, "ana@x.com", SituacaoDoUsuario.Ativo, "marketing-admin", "marketing-user", "rh-user", "gerente-de-compras");

		var resultado = await new ObterUsuarioHandler(gestao).HandleAsync(Ana);

		var tela = resultado.Value;
		tela.Setores.Should().HaveCount(2);
		tela.Setores.Single(s => s.Slug == "marketing").Papeis
			.Should().BeEquivalentTo([PapelNoSetor.Administrador, PapelNoSetor.Usuario]);
		tela.Setores.Single(s => s.Slug == "rh").Papeis.Should().Equal(PapelNoSetor.Usuario);
		tela.OutrosPerfis.Should().Equal("gerente-de-compras");
		tela.SlugsDeSetorDisponiveis.Should().BeEquivalentTo("marketing", "rh", "ti");
		tela.OutrosPerfisAtribuiveis.Should().Equal("all-users");
	}

	[Fact]
	public async Task ObterUsuario_Inexistente_DevolveNotFound()
	{
		var resultado = await new ObterUsuarioHandler(new GestaoDeAcessoFalsa()).HandleAsync(Guid.NewGuid());

		resultado.Error.Should().Be(IntranetErrors.Acesso.UsuarioNaoEncontrado);
	}
}
