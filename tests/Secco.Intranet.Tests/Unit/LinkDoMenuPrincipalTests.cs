using System.Text.RegularExpressions;
using AwesomeAssertions;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public sealed class LinkDoMenuPrincipalTests
{
	[Fact]
	public void Chave_PrefixoLinkMaisIdEmFormatoD()
	{
		var link = new LinkDoMenuPrincipal("Suporte", "https://suporte.exemplo.com.br", null, true);

		link.Chave.Should().Be("link-" + link.Id.ToString("D"));
		Regex.IsMatch(link.Chave, @"\A[a-z0-9-]{1,64}\z").Should().BeTrue();
	}

	[Fact]
	public void Construtor_RotuloVazio_Lanca()
	{
		var act = () => new LinkDoMenuPrincipal(" ", "https://suporte.exemplo.com.br", null, true);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_UrlVazia_Lanca()
	{
		var act = () => new LinkDoMenuPrincipal("Suporte", "", null, true);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Editar_TrocaOsQuatroCampos()
	{
		var link = new LinkDoMenuPrincipal("Suporte", "https://suporte.exemplo.com.br", null, true);

		link.Editar("Chamados", "/chamados", "bi-headset", false);

		link.Rotulo.Should().Be("Chamados");
		link.Url.Should().Be("/chamados");
		link.Icone.Should().Be("bi-headset");
		link.NovaAba.Should().BeFalse();
	}
}
