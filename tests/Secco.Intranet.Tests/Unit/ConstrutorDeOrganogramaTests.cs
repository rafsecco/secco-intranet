using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ConstrutorDeOrganogramaTests
{
	private static PessoaDto P(Guid id, string nome, Guid? gestor = null, bool gestorInativo = false) =>
		new(id, $"{nome}@x.com", nome, null, null, null, null, null, null, false, gestor, null, gestorInativo, true);

	private static string[] Nomes(IEnumerable<NoDoOrganograma> nos) => [.. nos.Select(no => no.Pessoa.Nome)];

	[Fact]
	public void ArvoreSimples_RaizComEquipeAninhada()
	{
		var ana = Guid.NewGuid();
		var bruno = Guid.NewGuid();
		var carla = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir([P(ana, "Ana"), P(bruno, "Bruno", ana), P(carla, "Carla", bruno)]);

		var raiz = organograma.Raizes.Should().ContainSingle().Subject;
		raiz.Pessoa.Nome.Should().Be("Ana");
		Nomes(raiz.Equipe).Should().Equal("Bruno");
		Nomes(raiz.Equipe[0].Equipe).Should().Equal("Carla");
		organograma.SemPosicao.Should().BeEmpty();
	}

	[Fact]
	public void QuemNaoTemGestorNemEquipe_VaiParaSemPosicao()
	{
		var ana = Guid.NewGuid();
		var solto = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir([P(ana, "Ana"), P(solto, "Solto")]);

		organograma.Raizes.Should().BeEmpty("Ana não tem equipe, então não é uma raiz");
		organograma.SemPosicao.Select(p => p.Nome).Should().BeEquivalentTo("Ana", "Solto");
	}

	[Fact]
	public void GestorInativo_AEquipeViraRaiz_ComAMarca()
	{
		var bruno = Guid.NewGuid();
		var carla = Guid.NewGuid();
		var fantasma = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir(
			[P(bruno, "Bruno", fantasma, gestorInativo: true), P(carla, "Carla", bruno)]);

		var raiz = organograma.Raizes.Should().ContainSingle().Subject;
		raiz.Pessoa.Nome.Should().Be("Bruno");
		raiz.Pessoa.GestorInativo.Should().BeTrue();
		Nomes(raiz.Equipe).Should().Equal("Carla");
	}

	[Fact]
	public void GestorInativoSemEquipe_VaiParaSemPosicao()
	{
		var bruno = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir([P(bruno, "Bruno", Guid.NewGuid(), gestorInativo: true)]);

		organograma.Raizes.Should().BeEmpty();
		organograma.SemPosicao.Should().ContainSingle(p => p.Nome == "Bruno");
	}

	[Fact]
	public void OrdenaPorNome_SemDiferenciarCaixa()
	{
		var chefe = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir(
			[P(chefe, "Chefe"), P(Guid.NewGuid(), "zélia", chefe), P(Guid.NewGuid(), "Ana", chefe), P(Guid.NewGuid(), "bruno", chefe)]);

		Nomes(organograma.Raizes[0].Equipe).Should().Equal("Ana", "bruno", "zélia");
	}

	[Fact]
	public void CicloJaGravadoNoBanco_NaoTravaENinguemSome()
	{
		// A reporta a B e B reporta a A, ambos ativos: nenhum é raiz. Sem a rede de segurança, os dois sumiriam.
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();

		var organograma = ConstrutorDeOrganograma.Construir([P(a, "A", b), P(b, "B", a)]);

		organograma.Raizes.Should().BeEmpty();
		organograma.SemPosicao.Select(p => p.Nome).Should().BeEquivalentTo("A", "B");
	}

	[Fact]
	public void CadeiaMaisFundaQueOTeto_TrunkaEColocaOResto_SemSumirNinguem()
	{
		var ids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();
		var pessoas = ids.Select((id, i) => P(id, $"P{i:00}", i == 0 ? null : ids[i - 1])).ToList();

		var organograma = ConstrutorDeOrganograma.Construir(pessoas);

		var naArvore = Contar(organograma.Raizes);
		(naArvore + organograma.SemPosicao.Count).Should().Be(30, "toda pessoa aparece em exatamente um lugar");
		naArvore.Should().BeLessThanOrEqualTo(RegrasDeGestor.ProfundidadeMaxima + 1);
	}

	[Fact]
	public void ListaVazia_OrganogramaVazio()
	{
		var organograma = ConstrutorDeOrganograma.Construir([]);

		organograma.Raizes.Should().BeEmpty();
		organograma.SemPosicao.Should().BeEmpty();
	}

	private static int Contar(IEnumerable<NoDoOrganograma> nos) => nos.Sum(no => 1 + Contar(no.Equipe));
}
