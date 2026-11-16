using System.Security.Claims;
using AwesomeAssertions;
using Secco.Intranet.Web.Diretorio;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>O endereço da conversa no Teams, montado só a partir do e-mail (a Intranet nunca chama a Microsoft).</summary>
public class LinkDeChatTests
{
	private static readonly Guid Tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private static LinkDeChat Teams(Guid? tenantId = null) =>
		new(new ChatOptions { Provedor = ProvedorDeChat.Teams, TenantId = tenantId });

	private static ClaimsPrincipal Pessoa(Guid id) =>
		new(new ClaimsIdentity([new Claim(SeccoClaims.Subject, id.ToString())], "Teste"));

	private static ClaimsPrincipal SemSub() => new(new ClaimsIdentity("Teste"));

	[Fact]
	public void Para_ProvedorDesligado_Nulo()
	{
		var link = new LinkDeChat(new ChatOptions());

		link.Para("ana@exemplo.test", Guid.NewGuid(), Pessoa(Guid.NewGuid())).Should().BeNull();
	}

	[Fact]
	public void Para_Teams_MontaOEndereco() =>
		Teams().Para("ana@exemplo.test", Guid.NewGuid(), Pessoa(Guid.NewGuid()))
			.Should().Be("https://teams.microsoft.com/l/chat/0/0?users=ana%40exemplo.test");

	[Fact]
	public void Para_Teams_ComTenant_AcrescentaTenantId() =>
		Teams(Tenant).Para("ana@exemplo.test", Guid.NewGuid(), Pessoa(Guid.NewGuid()))
			.Should().Be("https://teams.microsoft.com/l/chat/0/0?users=ana%40exemplo.test&tenantId=11111111-2222-3333-4444-555555555555");

	[Theory]
	[InlineData("ana+rh@exemplo.test", "ana%2Brh%40exemplo.test")]
	[InlineData("a&b@exemplo.test", "a%26b%40exemplo.test")]
	[InlineData("joão@exemplo.test", "jo%C3%A3o%40exemplo.test")]
	public void Para_EmailComCaracteresEspeciais_Escapa(string email, string escapado) =>
		Teams().Para(email, Guid.NewGuid(), Pessoa(Guid.NewGuid()))
			.Should().Be($"https://teams.microsoft.com/l/chat/0/0?users={escapado}");

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("sem-arroba")]
	public void Para_EmailInvalido_Nulo(string? email) =>
		Teams().Para(email, Guid.NewGuid(), Pessoa(Guid.NewGuid())).Should().BeNull();

	[Fact]
	public void Para_APropriaPessoa_Nulo()
	{
		var id = Guid.NewGuid();

		Teams().Para("ana@exemplo.test", id, Pessoa(id)).Should().BeNull();
	}

	[Fact]
	public void Para_QuemVeSemSub_DevolveOEndereco() =>
		Teams().Para("ana@exemplo.test", Guid.Empty, SemSub())
			.Should().Be("https://teams.microsoft.com/l/chat/0/0?users=ana%40exemplo.test");

	[Fact]
	public void NomeDoProvedor_Teams_ETeams_DesligadoENulo()
	{
		Teams().NomeDoProvedor.Should().Be("Teams");
		new LinkDeChat(new ChatOptions()).NomeDoProvedor.Should().BeNull();
	}
}
