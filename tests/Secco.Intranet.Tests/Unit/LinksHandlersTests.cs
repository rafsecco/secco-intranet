using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Links;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class LinksHandlersTests
{
	private sealed class Cenario
	{
		public ItemMenuRepositorioFalso Repo { get; } = new();

		public TrilhaDeAcessoFalsa Trilha { get; } = new();

		public ItemMenu Raiz { get; }

		public ItemMenu No { get; }

		public ItemMenu Documentos { get; }

		public ItemMenu DoAdmin { get; }

		public Guid SetorId => Raiz.SetorId;

		public Cenario(bool comNo = true)
		{
			Raiz = new ItemMenu(Guid.NewGuid(), null, "Financeiro", "financeiro", TipoDeItemMenu.Setor, null, null, 0);
			Documentos = new ItemMenu(SetorId, Raiz.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
			DoAdmin = new ItemMenu(SetorId, Raiz.Id, "Do admin", "do-admin", TipoDeItemMenu.Personalizado, "https://exemplo.test/admin", null, 2);
			No = new ItemMenu(SetorId, Raiz.Id, "Links", "links", TipoDeItemMenu.Links, null, null, 1);
			Repo.Itens.AddRange([Raiz, Documentos, DoAdmin]);

			if (comNo)
			{
				Repo.Itens.Add(No);
			}
		}

		public ItemMenu Grupo(string nome, int ordem, ItemMenu? pai = null)
		{
			var item = new ItemMenu(SetorId, (pai ?? No).Id, nome, nome.ToLowerInvariant(), TipoDeItemMenu.Personalizado, null, null, ordem);
			Repo.Itens.Add(item);

			return item;
		}

		public ItemMenu Link(string nome, int ordem, ItemMenu? pai = null, bool novaAba = false)
		{
			var item = new ItemMenu(
				SetorId, (pai ?? No).Id, nome, nome.ToLowerInvariant(), TipoDeItemMenu.Personalizado,
				"https://exemplo.test/" + nome.ToLowerInvariant(), null, ordem, NivelDeAcessoItemMenu.Leitura, novaAba);
			Repo.Itens.Add(item);

			return item;
		}

		public List<ItemMenu> Filhos(ItemMenu pai) =>
			[.. Repo.Itens.Where(i => i.ParentId == pai.Id).OrderBy(i => i.Ordem)];

		public CriarLinkHandler Criar() => new(Repo, Trilha);

		public EditarLinkHandler Editar() => new(Repo, Trilha);

		public MoverLinkHandler Mover() => new(Repo, Trilha);

		public ExcluirLinkHandler Excluir() => new(Repo, Trilha);
	}

	[Fact]
	public async Task Criar_LinkSemGrupo_EntraPorUltimoSobONo()
	{
		var c = new Cenario();
		c.Link("primeiro", 0);
		c.Grupo("bancos", 5);

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, "Portal ERP", "https://exemplo.test/erp", true, null));

		resultado.IsSuccess.Should().BeTrue();
		var criado = c.Repo.Itens.Single(i => i.Id == resultado.Value);
		criado.ParentId.Should().Be(c.No.Id);
		criado.Ordem.Should().Be(6);
		criado.Slug.Should().Be("portal-erp");
		criado.Tipo.Should().Be(TipoDeItemMenu.Personalizado);
		criado.NovaAba.Should().BeTrue();
		criado.Icone.Should().BeNull();
		c.Trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.MenuItemCriar);
	}

	[Fact]
	public async Task Criar_LinkEmGrupo_EntraPorUltimoNoGrupo()
	{
		var c = new Cenario();
		var grupo = c.Grupo("bancos", 0);
		c.Link("itau", 0, grupo);
		c.Link("bradesco", 3, grupo);

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, "Santander", "https://exemplo.test/s", false, grupo.Id));

		resultado.IsSuccess.Should().BeTrue();
		var criado = c.Repo.Itens.Single(i => i.Id == resultado.Value);
		criado.ParentId.Should().Be(grupo.Id);
		criado.Ordem.Should().Be(4);
	}

	[Fact]
	public async Task Criar_EnderecoComEspacosEMaiusculas_AceitaEApara()
	{
		var c = new Cenario();

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, "Site", "  HTTPS://Exemplo.com  ", false, null));

		resultado.IsSuccess.Should().BeTrue();
		c.Repo.Itens.Single(i => i.Id == resultado.Value).Rota.Should().Be("HTTPS://Exemplo.com");
	}

	[Theory]
	[InlineData("//exemplo.com")]
	[InlineData("javascript:alert(1)")]
	[InlineData("ftp://x")]
	[InlineData("exemplo.com")]
	public async Task Criar_EnderecoInvalido_RotaInvalida(string endereco)
	{
		var c = new Cenario();
		var antes = c.Repo.Itens.Count;

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, "Site", endereco, false, null));

		resultado.Error.Should().Be(IntranetErrors.Menu.RotaInvalida);
		c.Repo.Itens.Should().HaveCount(antes);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Criar_SemEndereco_EnderecoRequired(string? endereco)
	{
		var c = new Cenario();

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, "Site", endereco, false, null));

		resultado.Error.Should().Be(IntranetErrors.Links.EnderecoRequired);
	}

	[Theory]
	[InlineData("link")]
	[InlineData("fora")]
	[InlineData("inexistente")]
	public async Task Criar_GrupoInvalido_GrupoInvalido(string qual)
	{
		var c = new Cenario();
		var link = c.Link("itau", 0);
		var id = qual switch
		{
			"link" => link.Id,
			"fora" => c.DoAdmin.Id,
			_ => Guid.NewGuid(),
		};
		var antes = c.Repo.Itens.Count;

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, "Site", "https://exemplo.test/x", false, id));

		resultado.Error.Should().Be(IntranetErrors.Links.GrupoInvalido);
		c.Repo.Itens.Should().HaveCount(antes);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_NomeQueNaoGeraSlug_UsaLink()
	{
		var c = new Cenario();

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, "★★★", "https://exemplo.test/x", false, null));

		resultado.IsSuccess.Should().BeTrue();
		c.Repo.Itens.Single(i => i.Id == resultado.Value).Slug.Should().Be("link");
	}

	[Fact]
	public async Task Criar_NomeMaiorQue256_CampoMuitoLongo()
	{
		var c = new Cenario();

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, new string('a', 257), "https://exemplo.test/x", false, null));

		resultado.Error.Should().Be(IntranetErrors.Menu.CampoMuitoLongo("O nome", 256));
	}

	[Fact]
	public async Task Criar_SemNoLinks_NotFound()
	{
		var c = new Cenario(comNo: false);
		var antes = c.Repo.Itens.Count;

		var resultado = await c.Criar().HandleAsync(new CriarLinkCommand(c.SetorId, "Site", "https://exemplo.test/x", false, null));

		resultado.Error.Should().Be(IntranetErrors.Links.NotFound);
		c.Repo.Itens.Should().HaveCount(antes);
	}

	[Fact]
	public async Task CriarGrupo_NomeRepetido_GeraSlugComSufixo()
	{
		var c = new Cenario();
		var handler = new CriarGrupoDeLinksHandler(c.Repo, c.Trilha);
		c.Link("ultimo", 4);

		var primeiro = await handler.HandleAsync(c.SetorId, "Bancos");
		var segundo = await handler.HandleAsync(c.SetorId, "Bancos");

		primeiro.IsSuccess.Should().BeTrue();
		segundo.IsSuccess.Should().BeTrue();
		var g1 = c.Repo.Itens.Single(i => i.Id == primeiro.Value);
		var g2 = c.Repo.Itens.Single(i => i.Id == segundo.Value);
		g1.Slug.Should().Be("bancos");
		g2.Slug.Should().Be("bancos-2");
		g1.ParentId.Should().Be(c.No.Id);
		g1.Rota.Should().BeNull();
		g1.Ordem.Should().Be(5);
		g2.Ordem.Should().Be(6);
	}

	[Fact]
	public async Task Editar_TrocaDeGrupo_RenumeraOsDois()
	{
		var c = new Cenario();
		var origem = c.Grupo("origem", 0);
		var destino = c.Grupo("destino", 1);
		var a = c.Link("a", 0, origem);
		var b = c.Link("b", 4, origem);
		var d = c.Link("d", 0, destino);
		var e = c.Link("e", 7, destino);

		var resultado = await c.Editar().HandleAsync(new EditarLinkCommand(c.SetorId, a.Id, "A", "https://exemplo.test/novo", false, destino.Id));

		resultado.IsSuccess.Should().BeTrue();
		c.Filhos(origem).Select(i => (i.Id, i.Ordem)).Should().Equal((b.Id, 0));
		c.Filhos(destino).Select(i => (i.Id, i.Ordem)).Should().Equal((d.Id, 0), (e.Id, 1), (a.Id, 2));
		a.ParentId.Should().Be(destino.Id);
		a.Rota.Should().Be("https://exemplo.test/novo");
		c.Trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.MenuItemEditar);
	}

	[Fact]
	public async Task Editar_TrocaDeGrupoComNomeIgualEmConflitoDeSlug_GeraNovoSlug()
	{
		var c = new Cenario();
		var origem = c.Grupo("origem", 0);
		var destino = c.Grupo("destino", 1);
		var a = c.Link("erp", 0, origem);
		c.Link("erp", 0, destino);

		var resultado = await c.Editar().HandleAsync(new EditarLinkCommand(c.SetorId, a.Id, "erp", a.Rota, false, destino.Id));

		resultado.IsSuccess.Should().BeTrue();
		a.Slug.Should().Be("erp-2");
	}

	[Fact]
	public async Task Editar_NomeIgual_MantemOSlug()
	{
		var c = new Cenario();
		var a = c.Link("erp", 0);

		var resultado = await c.Editar().HandleAsync(new EditarLinkCommand(c.SetorId, a.Id, "erp", "https://exemplo.test/outro", true, null));

		resultado.IsSuccess.Should().BeTrue();
		a.Slug.Should().Be("erp");
		a.NovaAba.Should().BeTrue();
		a.ParentId.Should().Be(c.No.Id);
	}

	[Fact]
	public async Task Editar_ParaOProprioIdComoGrupo_GrupoInvalido()
	{
		var c = new Cenario();
		var a = c.Link("a", 0);

		var resultado = await c.Editar().HandleAsync(new EditarLinkCommand(c.SetorId, a.Id, "A", "https://exemplo.test/a", false, a.Id));

		resultado.Error.Should().Be(IntranetErrors.Links.GrupoInvalido);
		a.Nome.Should().Be("a");
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_LinkSemEndereco_EnderecoRequired()
	{
		var c = new Cenario();
		var a = c.Link("a", 0);

		var resultado = await c.Editar().HandleAsync(new EditarLinkCommand(c.SetorId, a.Id, "A", "  ", false, null));

		resultado.Error.Should().Be(IntranetErrors.Links.EnderecoRequired);
		a.Rota.Should().Be("https://exemplo.test/a");
	}

	[Theory]
	[InlineData("documentos")]
	[InlineData("admin")]
	[InlineData("no")]
	[InlineData("raiz")]
	public async Task Editar_ItemForaDoTrecho_NotFound(string qual)
	{
		var c = new Cenario();
		var alvo = qual switch
		{
			"documentos" => c.Documentos,
			"admin" => c.DoAdmin,
			"no" => c.No,
			_ => c.Raiz,
		};
		var nomeAntes = alvo.Nome;
		var rotaAntes = alvo.Rota;

		var resultado = await c.Editar().HandleAsync(new EditarLinkCommand(c.SetorId, alvo.Id, "Invadido", "https://exemplo.test/invadido", true, null));

		resultado.Error.Should().Be(IntranetErrors.Links.NotFound);
		alvo.Nome.Should().Be(nomeAntes);
		alvo.Rota.Should().Be(rotaAntes);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Editar_ItemDeOutroSetor_NotFound()
	{
		var c = new Cenario();
		var outro = new Cenario();
		var alheio = outro.Link("alheio", 0);
		c.Repo.Itens.Add(alheio);

		var resultado = await c.Editar().HandleAsync(new EditarLinkCommand(c.SetorId, alheio.Id, "Invadido", "https://exemplo.test/i", false, null));

		resultado.Error.Should().Be(IntranetErrors.Links.NotFound);
		alheio.Nome.Should().Be("alheio");
	}

	[Fact]
	public async Task Editar_Grupo_SoTrocaONome()
	{
		var c = new Cenario();
		var outro = c.Grupo("outro", 0);
		var grupo = c.Grupo("bancos", 1);
		var filho = c.Link("itau", 0, grupo);

		var resultado = await c.Editar().HandleAsync(new EditarLinkCommand(c.SetorId, grupo.Id, "Instituições", "https://exemplo.test/ignorado", true, outro.Id));

		resultado.IsSuccess.Should().BeTrue();
		grupo.Nome.Should().Be("Instituições");
		grupo.Slug.Should().Be("instituicoes");
		grupo.Rota.Should().BeNull();
		grupo.NovaAba.Should().BeFalse();
		grupo.ParentId.Should().Be(c.No.Id);
		grupo.Ordem.Should().Be(1);
		filho.ParentId.Should().Be(grupo.Id);
	}

	[Fact]
	public async Task Mover_ComBuracosNaOrdem_TrocaSoComOVizinho()
	{
		var c = new Cenario();
		var a = c.Link("a", 0);
		var b = c.Link("b", 2);
		var d = c.Link("d", 3);

		var resultado = await c.Mover().HandleAsync(c.SetorId, d.Id, paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		c.Filhos(c.No).Select(i => (i.Id, i.Ordem)).Should().Equal((a.Id, 0), (d.Id, 1), (b.Id, 2));
		c.Trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.MenuItemMover);
	}

	[Fact]
	public async Task Mover_PrimeiroParaCima_NaoMuda()
	{
		var c = new Cenario();
		var a = c.Link("a", 0);
		var b = c.Link("b", 2);

		var resultado = await c.Mover().HandleAsync(c.SetorId, a.Id, paraCima: true);

		resultado.IsSuccess.Should().BeTrue();
		a.Ordem.Should().Be(0);
		b.Ordem.Should().Be(2);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Mover_ItemForaDoTrecho_NotFound()
	{
		var c = new Cenario();
		var ordemAntes = c.DoAdmin.Ordem;

		var resultado = await c.Mover().HandleAsync(c.SetorId, c.DoAdmin.Id, paraCima: true);

		resultado.Error.Should().Be(IntranetErrors.Links.NotFound);
		c.DoAdmin.Ordem.Should().Be(ordemAntes);
		c.Documentos.Ordem.Should().Be(0);
	}

	[Fact]
	public async Task Excluir_GrupoComLinks_ItemComFilhos()
	{
		var c = new Cenario();
		var grupo = c.Grupo("bancos", 0);
		c.Link("itau", 0, grupo);

		var resultado = await c.Excluir().HandleAsync(c.SetorId, grupo.Id);

		resultado.Error.Should().Be(IntranetErrors.Menu.ItemComFilhos);
		c.Repo.Itens.Should().Contain(grupo);
	}

	[Fact]
	public async Task Excluir_Link_RemoveEAudita()
	{
		var c = new Cenario();
		var link = c.Link("itau", 0);

		var resultado = await c.Excluir().HandleAsync(c.SetorId, link.Id);

		resultado.IsSuccess.Should().BeTrue();
		c.Repo.Itens.Should().NotContain(link);
		c.Trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.MenuItemExcluir);
	}

	[Theory]
	[InlineData("no")]
	[InlineData("admin")]
	[InlineData("documentos")]
	public async Task Excluir_NoLinks_NotFound(string qual)
	{
		var c = new Cenario();
		var alvo = qual switch
		{
			"no" => c.No,
			"admin" => c.DoAdmin,
			_ => c.Documentos,
		};

		var resultado = await c.Excluir().HandleAsync(c.SetorId, alvo.Id);

		resultado.Error.Should().Be(IntranetErrors.Links.NotFound);
		c.Repo.Itens.Should().Contain(alvo);
		c.Trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Listar_DevolveEntradasNaOrdem_GruposComSeusLinks()
	{
		var c = new Cenario();
		var solto = c.Link("solto", 1, novaAba: true);
		var grupo = c.Grupo("bancos", 0);
		var vazio = c.Grupo("vazio", 2);
		var b = c.Link("b", 1, grupo);
		var a = c.Link("a", 0, grupo);

		var resultado = await new ListarLinksDoSetorHandler(c.Repo).HandleAsync(c.SetorId);

		resultado.IsSuccess.Should().BeTrue();
		var dto = resultado.Value;
		dto.Entradas.Should().HaveCount(3);
		dto.Entradas[0].Grupo!.Id.Should().Be(grupo.Id);
		dto.Entradas[0].Link.Should().BeNull();
		dto.Entradas[0].Grupo!.Links.Select(l => l.Id).Should().Equal(a.Id, b.Id);
		dto.Entradas[0].Grupo!.Links.Should().OnlyContain(l => l.GrupoId == grupo.Id);
		dto.Entradas[1].Link!.Id.Should().Be(solto.Id);
		dto.Entradas[1].Link!.NovaAba.Should().BeTrue();
		dto.Entradas[1].Link!.GrupoId.Should().BeNull();
		dto.Entradas[2].Grupo!.Id.Should().Be(vazio.Id);
		dto.Grupos.Select(g => g.Id).Should().Equal(grupo.Id, vazio.Id);
	}

	[Fact]
	public async Task Listar_SemNoLinks_NotFound()
	{
		var c = new Cenario(comNo: false);

		var resultado = await new ListarLinksDoSetorHandler(c.Repo).HandleAsync(c.SetorId);

		resultado.Error.Should().Be(IntranetErrors.Links.NotFound);
	}
}
