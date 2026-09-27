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
}
