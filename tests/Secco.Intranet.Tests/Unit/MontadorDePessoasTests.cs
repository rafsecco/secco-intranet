using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class MontadorDePessoasTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();

	private static SetorDto Setor(string nome, bool ativo = true) =>
		new(Guid.NewGuid(), nome, nome.ToLowerInvariant(), "bi-x", false, ativo, DateTimeOffset.UtcNow);

	private static PerfilColaborador Perfil(Guid usuario, string? cargo = null, Guid? setor = null, Guid? gestor = null)
	{
		var perfil = new PerfilColaborador(usuario);
		perfil.EditarDadosFuncionais(cargo, setor, gestor);

		return perfil;
	}

	private static UsuarioParaDiretorio Usuario(Guid id, AtributosDoEntra? entra = null) =>
		new(id, $"{id}@x.com", null, entra);

	[Fact]
	public void Montar_EntraPreenchido_CargoVenceEMarca()
	{
		var pessoa = MontadorDePessoas.Montar(
			[Usuario(Ana, new AtributosDoEntra("Diretora", null, null, null))],
			[Perfil(Ana, cargo: "Analista")], []).Single();

		pessoa.Cargo.Should().Be("Diretora");
		pessoa.CargoDoEntra.Should().BeTrue();
	}

	[Fact]
	public void Montar_EntraPreenchido_SetorVenceEMarca()
	{
		var ti = Setor("TI");
		var rh = Setor("RH");

		var pessoa = MontadorDePessoas.Montar(
			[Usuario(Ana, new AtributosDoEntra(null, "ti", null, null))],
			[Perfil(Ana, setor: rh.Id)], [ti, rh]).Single();

		pessoa.SetorId.Should().Be(ti.Id);
		pessoa.SetorNome.Should().Be("TI");
		pessoa.SetorDoEntra.Should().BeTrue();
	}

	[Fact]
	public void Montar_EntraPreenchido_GestorVenceEMarca()
	{
		var pessoa = MontadorDePessoas.Montar(
			[Usuario(Ana, new AtributosDoEntra(null, null, null, Bruno)), Usuario(Bruno), Usuario(Carla)],
			[Perfil(Ana, gestor: Carla)], []).Single(p => p.UsuarioId == Ana);

		pessoa.GestorUsuarioId.Should().Be(Bruno);
		pessoa.GestorNome.Should().Be($"{Bruno}@x.com");
		pessoa.GestorDoEntra.Should().BeTrue();
		pessoa.GestorInativo.Should().BeFalse();
	}

	[Fact]
	public void Montar_EntraVazio_UsaPerfil()
	{
		var ti = Setor("TI");

		var pessoa = MontadorDePessoas.Montar(
			[Usuario(Ana, new AtributosDoEntra(null, null, null, null)), Usuario(Bruno)],
			[Perfil(Ana, "Analista", ti.Id, Bruno)], [ti]).Single(p => p.UsuarioId == Ana);

		pessoa.Cargo.Should().Be("Analista");
		pessoa.SetorId.Should().Be(ti.Id);
		pessoa.GestorUsuarioId.Should().Be(Bruno);
		pessoa.CargoDoEntra.Should().BeFalse();
		pessoa.SetorDoEntra.Should().BeFalse();
		pessoa.GestorDoEntra.Should().BeFalse();
		pessoa.Telefone.Should().BeNull();
	}

	[Fact]
	public void Montar_SemPerfilSoEntra_PreencheTudo()
	{
		var ti = Setor("TI");

		var pessoa = MontadorDePessoas.Montar(
			[Usuario(Ana, new AtributosDoEntra("Dev", "TI", "123", Bruno)), Usuario(Bruno)],
			[], [ti]).Single(p => p.UsuarioId == Ana);

		pessoa.TemPerfil.Should().BeFalse();
		pessoa.Cargo.Should().Be("Dev");
		pessoa.SetorId.Should().Be(ti.Id);
		pessoa.GestorUsuarioId.Should().Be(Bruno);
		pessoa.Telefone.Should().Be("123");
	}

	[Fact]
	public void Montar_DepartamentoSemSetor_SetorDoPerfilEditavel()
	{
		var rh = Setor("RH");

		var pessoa = MontadorDePessoas.Montar(
			[Usuario(Ana, new AtributosDoEntra(null, "Inexistente", null, null))],
			[Perfil(Ana, setor: rh.Id)], [rh]).Single();

		pessoa.SetorId.Should().Be(rh.Id);
		pessoa.SetorDoEntra.Should().BeFalse();
	}

	[Fact]
	public void Montar_Telefone_SoDoEntra()
	{
		var semEntra = MontadorDePessoas.Montar([Usuario(Ana)], [Perfil(Ana)], []).Single();
		var comEntra = MontadorDePessoas.Montar(
			[Usuario(Ana, new AtributosDoEntra(null, null, "555", null))], [], []).Single();

		semEntra.Telefone.Should().BeNull();
		comEntra.Telefone.Should().Be("555");
	}

	[Fact]
	public void Montar_GestorDoEntraForaDosAtivos_GestorInativo()
	{
		var fantasma = Guid.NewGuid();

		var pessoa = MontadorDePessoas.Montar(
			[Usuario(Ana, new AtributosDoEntra(null, null, null, fantasma))], [], []).Single();

		pessoa.GestorInativo.Should().BeTrue();
		pessoa.GestorNome.Should().BeNull();
		pessoa.GestorDoEntra.Should().BeTrue();
	}
}
