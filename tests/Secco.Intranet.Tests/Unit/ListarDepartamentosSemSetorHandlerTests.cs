using AwesomeAssertions;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ListarDepartamentosSemSetorHandlerTests
{
	private static AtributosDoEntra Dep(string? departamento) => new(null, departamento, null, null);

	private static ListarDepartamentosSemSetorHandler Montar(UsuariosParaDiretorioFalso usuarios, SetoresFalsos setores) =>
		new(usuarios, setores);

	[Fact]
	public async Task DepartamentosSemSetor_ContaPessoasAtivas()
	{
		var usuarios = new UsuariosParaDiretorioFalso()
			.Com(Guid.NewGuid(), "a@x.com", entra: Dep("Jurídico"))
			.Com(Guid.NewGuid(), "b@x.com", entra: Dep("Jurídico"))
			.Com(Guid.NewGuid(), "c@x.com", entra: Dep("Compras"));

		var resultado = await Montar(usuarios, new SetoresFalsos()).HandleAsync();

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Should().Equal(new DepartamentoSemSetorDto("Compras", 1, false), new DepartamentoSemSetorDto("Jurídico", 2, false));
	}

	[Fact]
	public async Task DepartamentosSemSetor_Casados_NaoAparecem()
	{
		var setores = new SetoresFalsos();
		setores.Com("Financeiro", "financeiro");
		var usuarios = new UsuariosParaDiretorioFalso()
			.Com(Guid.NewGuid(), "a@x.com", entra: Dep("financeiro"))
			.Com(Guid.NewGuid(), "b@x.com", entra: Dep("Compras"));

		var resultado = await Montar(usuarios, setores).HandleAsync();

		resultado.Value.Should().ContainSingle().Which.Departamento.Should().Be("Compras");
	}

	[Fact]
	public async Task DepartamentosSemSetor_Ambiguo_MarcadoAParte()
	{
		var setores = new SetoresFalsos();
		setores.Com("Compras", "compras");
		setores.Com("COMPRAS", "compras-2");
		var usuarios = new UsuariosParaDiretorioFalso().Com(Guid.NewGuid(), "a@x.com", entra: Dep("Compras"));

		var resultado = await Montar(usuarios, setores).HandleAsync();

		resultado.Value.Should().ContainSingle().Which.Ambiguo.Should().BeTrue();
	}

	[Fact]
	public async Task DepartamentosSemSetor_GrafiasDiferentes_AgrupaPeloNormalizado()
	{
		var usuarios = new UsuariosParaDiretorioFalso()
			.Com(Guid.NewGuid(), "a@x.com", entra: Dep("Jurídico"))
			.Com(Guid.NewGuid(), "b@x.com", entra: Dep(" JURIDICO "))
			.Com(Guid.NewGuid(), "c@x.com", entra: Dep("juridico"));

		var resultado = await Montar(usuarios, new SetoresFalsos()).HandleAsync();

		resultado.Value.Should().ContainSingle().Which.Should().Be(new DepartamentoSemSetorDto("Jurídico", 3, false));
	}

	[Fact]
	public async Task DepartamentosSemSetor_SemEntra_Vazio()
	{
		var usuarios = new UsuariosParaDiretorioFalso()
			.Com(Guid.NewGuid(), "a@x.com")
			.Com(Guid.NewGuid(), "b@x.com", entra: Dep(null))
			.Com(Guid.NewGuid(), "c@x.com", entra: Dep("  "));

		var resultado = await Montar(usuarios, new SetoresFalsos()).HandleAsync();

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Should().BeEmpty();
	}
}
