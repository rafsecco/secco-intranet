using AwesomeAssertions;
using Secco.Intranet.Domain.Diretorio;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class PerfilColaboradorTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();

	[Fact]
	public void Novo_ComUsuarioVazio_Recusa()
	{
		var criar = () => new PerfilColaborador(Guid.Empty);

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Novo_NasceSemNadaPreenchido()
	{
		var perfil = new PerfilColaborador(Ana);

		perfil.UsuarioId.Should().Be(Ana);
		perfil.Ramal.Should().BeNull();
		perfil.Cargo.Should().BeNull();
		perfil.UpdatedAt.Should().BeNull();
	}

	[Fact]
	public void EditarContato_AparaEDevolveOsCamposAlterados()
	{
		var perfil = new PerfilColaborador(Ana);

		var alterados = perfil.EditarContato(" 2100 ", "  Sobre a Ana ");

		alterados.Should().Equal(PerfilColaborador.CampoRamal, PerfilColaborador.CampoSobre);
		perfil.Ramal.Should().Be("2100");
		perfil.Sobre.Should().Be("Sobre a Ana");
		perfil.UpdatedAt.Should().NotBeNull();
	}

	[Fact]
	public void EditarContato_TextoEmBranco_ViraNulo()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.EditarContato("1", "sobre");

		var alterados = perfil.EditarContato("", "   ");

		alterados.Should().Equal(PerfilColaborador.CampoRamal, PerfilColaborador.CampoSobre);
		perfil.Ramal.Should().BeNull();
		perfil.Sobre.Should().BeNull();
	}

	[Fact]
	public void EditarContato_SemMudanca_NaoDevolveCampoNemAtualizaData()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.EditarContato("2100", null);
		var atualizado = perfil.UpdatedAt;

		var alterados = perfil.EditarContato("2100", null);

		alterados.Should().BeEmpty();
		perfil.UpdatedAt.Should().Be(atualizado);
	}

	[Theory]
	[InlineData(PerfilColaborador.RamalMaxLength + 1, 0)]
	[InlineData(0, PerfilColaborador.SobreMaxLength + 1)]
	public void EditarContato_AcimaDoLimite_Recusa(int ramal, int sobre)
	{
		var perfil = new PerfilColaborador(Ana);

		var editar = () => perfil.EditarContato(new string('1', ramal), new string('s', sobre));

		editar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void EditarContato_Recusado_NaoAlteraNada()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.EditarContato("1", null);

		var editar = () => perfil.EditarContato("2", new string('s', PerfilColaborador.SobreMaxLength + 1));

		editar.Should().Throw<DomainInvariantException>();
		perfil.Ramal.Should().Be("1", "a validação acontece antes de qualquer atribuição");
	}

	[Fact]
	public void EditarDadosFuncionais_DefineEDevolveOsCampos()
	{
		var perfil = new PerfilColaborador(Ana);
		var setor = Guid.NewGuid();

		var alterados = perfil.EditarDadosFuncionais(" Analista ", setor, Bruno);

		alterados.Should().Equal(PerfilColaborador.CampoCargo, PerfilColaborador.CampoSetor, PerfilColaborador.CampoGestor);
		perfil.Cargo.Should().Be("Analista");
		perfil.SetorId.Should().Be(setor);
		perfil.GestorUsuarioId.Should().Be(Bruno);
	}

	[Fact]
	public void EditarDadosFuncionais_NulosLimpamOsCampos()
	{
		var perfil = new PerfilColaborador(Ana);
		perfil.EditarDadosFuncionais("Analista", Guid.NewGuid(), Bruno);

		var alterados = perfil.EditarDadosFuncionais(null, null, null);

		alterados.Should().HaveCount(3);
		perfil.SetorId.Should().BeNull();
		perfil.GestorUsuarioId.Should().BeNull();
	}

	[Fact]
	public void EditarDadosFuncionais_GuidVazio_TratadoComoNulo()
	{
		var perfil = new PerfilColaborador(Ana);

		var alterados = perfil.EditarDadosFuncionais(null, Guid.Empty, Guid.Empty);

		alterados.Should().BeEmpty();
		perfil.SetorId.Should().BeNull();
	}

	[Fact]
	public void EditarDadosFuncionais_GestorEhOProprio_Recusa()
	{
		var perfil = new PerfilColaborador(Ana);

		var editar = () => perfil.EditarDadosFuncionais(null, null, Ana);

		editar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void EditarDadosFuncionais_CargoAcimaDoLimite_Recusa()
	{
		var perfil = new PerfilColaborador(Ana);

		var editar = () => perfil.EditarDadosFuncionais(new string('c', PerfilColaborador.CargoMaxLength + 1), null, null);

		editar.Should().Throw<DomainInvariantException>();
	}
}
