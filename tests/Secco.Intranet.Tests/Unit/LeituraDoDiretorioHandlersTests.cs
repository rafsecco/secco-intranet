using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Domain.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LeituraDoDiretorioHandlersTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();
	private static readonly Guid Desativado = Guid.NewGuid();

	private static PerfilColaborador Perfil(Guid usuario, string? nome, string? cargo = null, Guid? setor = null, Guid? gestor = null)
	{
		var perfil = new PerfilColaborador(usuario);
		perfil.EditarContato(nome, null, null);
		perfil.EditarDadosFuncionais(cargo, setor, gestor);

		return perfil;
	}

	private static (ListarPessoasHandler Listar, ObterPessoaHandler Obter, SetoresFalsos Setores) Montar(
		UsuariosParaDiretorioFalso usuarios, PerfisColaboradorFalso perfis)
	{
		var setores = new SetoresFalsos();

		return (new ListarPessoasHandler(usuarios, perfis, setores), new ObterPessoaHandler(usuarios, perfis, setores), setores);
	}

	[Fact]
	public async Task UsuarioSemPerfil_AparecePeloEmail()
	{
		var (listar, _, _) = Montar(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), new PerfisColaboradorFalso());

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		var pessoa = resultado.Value.Pagina.Items.Should().ContainSingle().Subject;
		pessoa.Nome.Should().Be("ana@x.com");
		pessoa.TemPerfil.Should().BeFalse();
	}

	[Fact]
	public async Task UsuarioSemEmail_CaiNoId_SemQuebrar()
	{
		var (listar, _, _) = Montar(new UsuariosParaDiretorioFalso().Com(Ana, string.Empty), new PerfisColaboradorFalso());

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		resultado.Value.Pagina.Items.Single().Nome.Should().Be(Ana.ToString());
	}

	[Fact]
	public async Task PerfilDeUsuarioDesativado_NaoAparece()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, "Ana Ribeiro")).Com(Perfil(Desativado, "Fantasma"));
		var (listar, _, _) = Montar(usuarios, perfis);

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		resultado.Value.Pagina.Items.Select(p => p.Nome).Should().Equal("Ana Ribeiro");
	}

	[Fact]
	public async Task OrdenaPorNome_SemDiferenciarCaixa()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "a@x.com").Com(Bruno, "b@x.com").Com(Carla, "c@x.com");
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, "zélia")).Com(Perfil(Bruno, "Bruno")).Com(Perfil(Carla, "ana"));
		var (listar, _, _) = Montar(usuarios, perfis);

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		resultado.Value.Pagina.Items.Select(p => p.Nome).Should().Equal("ana", "Bruno", "zélia");
	}

	[Fact]
	public async Task Busca_AchaPorNomeCargoSetorEEmail()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@y.com").Com(Carla, "carla@z.com");
		var (listar, _, setores) = Montar(usuarios, new PerfisColaboradorFalso());
		var financeiro = setores.Com("Financeiro", "financeiro");
		var perfis = new PerfisColaboradorFalso()
			.Com(Perfil(Ana, "Ana Ribeiro", "Controller"))
			.Com(Perfil(Bruno, "Bruno", "Analista", financeiro.Id))
			.Com(Perfil(Carla, "Carla"));
		var handler = new ListarPessoasHandler(usuarios, perfis, setores);

		(await handler.HandleAsync(new ListarPessoasQuery("ribeiro", null, 1))).Value.Pagina.Items.Should().ContainSingle();
		(await handler.HandleAsync(new ListarPessoasQuery("controller", null, 1))).Value.Pagina.Items.Should().ContainSingle();
		(await handler.HandleAsync(new ListarPessoasQuery("FINANCEIRO", null, 1))).Value.Pagina.Items.Should().ContainSingle(p => p.UsuarioId == Bruno);
		(await handler.HandleAsync(new ListarPessoasQuery("@z.com", null, 1))).Value.Pagina.Items.Should().ContainSingle(p => p.UsuarioId == Carla);
	}

	[Fact]
	public async Task FiltroPorSetor_RestringePeloSlug()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com");
		var setores = new SetoresFalsos();
		var rh = setores.Com("RH", "rh");
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, "Ana", setor: rh.Id)).Com(Perfil(Bruno, "Bruno"));
		var handler = new ListarPessoasHandler(usuarios, perfis, setores);

		var resultado = await handler.HandleAsync(new ListarPessoasQuery(null, "RH", 1));

		resultado.Value.Pagina.Items.Select(p => p.UsuarioId).Should().Equal(Ana);
		resultado.Value.Setores.Should().ContainSingle(s => s.Slug == "rh");
	}

	[Fact]
	public async Task SetorDesativado_AindaMostraNaLotacao_MasNaoNoFiltro()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com");
		var setores = new SetoresFalsos();
		var antigo = setores.Com("Antigo", "antigo", ativo: false);
		var perfis = new PerfisColaboradorFalso().Com(Perfil(Ana, "Ana", setor: antigo.Id));
		var handler = new ListarPessoasHandler(usuarios, perfis, setores);

		var resultado = await handler.HandleAsync(new ListarPessoasQuery(null, null, 1));

		var pessoa = resultado.Value.Pagina.Items.Single();
		pessoa.SetorNome.Should().Be("Antigo");
		pessoa.SetorAtivo.Should().BeFalse();
		resultado.Value.Setores.Should().BeEmpty("o filtro só oferece setores ativos");
	}

	[Fact]
	public async Task PaginaAlemDoFim_DevolveVazioSemQuebrar()
	{
		var (listar, _, _) = Montar(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), new PerfisColaboradorFalso());

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 99));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Pagina.Items.Should().BeEmpty();
		resultado.Value.Pagina.TotalCount.Should().Be(1);
	}

	[Fact]
	public async Task PaginaTem24()
	{
		var usuarios = new UsuariosParaDiretorioFalso();

		for (var i = 0; i < 30; i++)
		{
			usuarios.Com(Guid.NewGuid(), $"u{i:00}@x.com");
		}

		var (listar, _, _) = Montar(usuarios, new PerfisColaboradorFalso());

		var resultado = await listar.HandleAsync(new ListarPessoasQuery(null, null, 1));

		resultado.Value.Pagina.Items.Should().HaveCount(24);
		resultado.Value.Pagina.TotalCount.Should().Be(30);
	}

	[Fact]
	public async Task SecureGateForaDoAr_PropagaOErro_NaoEListaVazia()
	{
		var usuarios = new UsuariosParaDiretorioFalso { FalharCom = IntranetErrors.Acesso.Indisponivel };
		var (listar, obter, _) = Montar(usuarios, new PerfisColaboradorFalso());

		(await listar.HandleAsync(new ListarPessoasQuery(null, null, 1))).Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		(await obter.HandleAsync(Ana)).Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Obter_TrazGestorEEquipe_ComGestorInativoMarcado()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com").Com(Carla, "carla@x.com");
		var perfis = new PerfisColaboradorFalso()
			.Com(Perfil(Ana, "Ana"))
			.Com(Perfil(Bruno, "Bruno", gestor: Ana))
			.Com(Perfil(Carla, "Carla", gestor: Desativado));
		var (_, obter, _) = Montar(usuarios, perfis);

		var deAna = (await obter.HandleAsync(Ana)).Value;
		var deBruno = (await obter.HandleAsync(Bruno)).Value;
		var deCarla = (await obter.HandleAsync(Carla)).Value;

		deAna.Equipe.Select(p => p.UsuarioId).Should().Equal(Bruno);
		deBruno.Gestor!.UsuarioId.Should().Be(Ana);
		deBruno.Pessoa.GestorNome.Should().Be("Ana");
		deCarla.Gestor.Should().BeNull();
		deCarla.Pessoa.GestorInativo.Should().BeTrue("o gestor definido não é mais um usuário ativo");
	}

	[Fact]
	public async Task Obter_UsuarioDesativadoOuInexistente_NotFound()
	{
		var (_, obter, _) = Montar(new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com"), new PerfisColaboradorFalso());

		(await obter.HandleAsync(Desativado)).Error.Should().Be(IntranetErrors.Diretorio.PessoaNaoEncontrada);
	}
}
