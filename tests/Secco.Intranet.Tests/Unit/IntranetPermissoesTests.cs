using System.Reflection;
using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.SharedKernel.Authorization;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class IntranetPermissoesTests
{
	[Fact]
	public void Setor_Read_ComponhaNoFormatoCanonico()
	{
		var permissao = IntranetPermissoes.Setor.Read("recursos-humanos");

		permissao.Should().Be("setor-recursos-humanos:read");
		SeccoPermissions.IsValid(permissao).Should().BeTrue();
	}

	[Fact]
	public void Setor_Write_ComponhaNoFormatoCanonico()
	{
		var permissao = IntranetPermissoes.Setor.Write("ti");

		permissao.Should().Be("setor-ti:write");
		SeccoPermissions.IsValid(permissao).Should().BeTrue();
	}

	[Theory]
	[InlineData("diretorio:read")]
	[InlineData("diretorio:manage")]
	[InlineData("inventario:read")]
	[InlineData("setores:read")]
	public void PermissoesFixas_EstaoNoFormatoCanonico(string permissao)
	{
		SeccoPermissions.IsValid(permissao).Should().BeTrue();
	}

	[Fact]
	public void Diretorio_NomesBatemComOPrometidoNaSpec()
	{
		IntranetPermissoes.Diretorio.Read.Should().Be("diretorio:read");
		IntranetPermissoes.Diretorio.Manage.Should().Be("diretorio:manage");
	}

	[Fact]
	public void Inventario_NomeBateComOPrometidoNaSpec()
	{
		IntranetPermissoes.Inventario.Read.Should().Be("inventario:read");
	}

	[Fact]
	public void SetorReadGlobal_NomeBateComOPrometidoNaSpec()
	{
		IntranetPermissoes.Setor.ReadGlobal.Should().Be("setores:read");
	}

	[Fact]
	public void Catalogo_NaoInclueAsPorSetor_SaoDinamicasPorSlug()
	{
		IntranetPermissoes.Catalogo.Should().NotContain(p => p.StartsWith("setor-", StringComparison.Ordinal));
		IntranetPermissoes.Catalogo.Should().Contain(IntranetPermissoes.Diretorio.Read);
		IntranetPermissoes.Catalogo.Should().Contain(IntranetPermissoes.Diretorio.Manage);
		IntranetPermissoes.Catalogo.Should().Contain(IntranetPermissoes.Inventario.Read);
		IntranetPermissoes.Catalogo.Should().Contain(IntranetPermissoes.Setor.ReadGlobal);
	}

	/// <summary>
	/// O catálogo publicado no SecureGate (ADR-0039 da plataforma) precisa conter toda permissão fixa
	/// do produto: uma constante esquecida fora dele é recusada pelo SecureGate ao ser atribuída.
	/// </summary>
	[Fact]
	public void Catalog_ContemTodaConstanteDePermissao_ComDescricao()
	{
		var constantes = typeof(IntranetPermissoes)
			.GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
			.SelectMany(tipo => tipo.GetFields(BindingFlags.Public | BindingFlags.Static))
			.Where(campo => campo is { IsLiteral: true, IsInitOnly: false } && campo.FieldType == typeof(string))
			.Select(campo => (string)campo.GetRawConstantValue()!)
			.ToList();

		constantes.Should().NotBeEmpty();
		IntranetPermissoes.Catalog.Select(p => p.Name).Should().BeEquivalentTo(constantes);
		IntranetPermissoes.Catalog.Should().OnlyContain(p =>
			(SeccoPermissions.IsValid(p.Name) || SeccoPermissions.IsValidFamily(p.Name)) && !string.IsNullOrWhiteSpace(p.Description));
		IntranetPermissoes.Catalog.Select(p => p.Name).Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public void Catalogo_DaTelaEOMesmoDoCatalogPublicado()
	{
		IntranetPermissoes.Catalogo.Should().Equal(
			IntranetPermissoes.Catalog.Select(p => p.Name).Where(nome => !SeccoPermissions.IsValidFamily(nome)),
			"família não é permissão atribuível: as caixas da tela são só os nomes fixos");
	}

	/// <summary>
	/// As permissões por setor nascem da família publicada no catálogo (ADR-0043 da plataforma): o
	/// SecureGate aceita a instância porque ela casa com a família.
	/// </summary>
	[Theory]
	[InlineData("ti")]
	[InlineData("recursos-humanos")]
	public void Setor_ReadEWrite_CasamComAsFamiliasDoCatalogo(string slug)
	{
		IntranetPermissoes.Catalog.Select(p => p.Name).Should()
			.Contain(IntranetPermissoes.Setor.FamiliaRead).And.Contain(IntranetPermissoes.Setor.FamiliaWrite);

		SeccoPermissions.MatchesFamily(IntranetPermissoes.Setor.FamiliaRead, IntranetPermissoes.Setor.Read(slug)).Should().BeTrue();
		SeccoPermissions.MatchesFamily(IntranetPermissoes.Setor.FamiliaWrite, IntranetPermissoes.Setor.Write(slug)).Should().BeTrue();
	}
}
