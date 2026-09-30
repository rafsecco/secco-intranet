using AwesomeAssertions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class FederacaoHandlerTests
{
	private static readonly Guid DirectoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");

	[Fact]
	public async Task Obter_NuncaConfigurada_DevolveDesabilitadaSemDirectoryId()
	{
		var handler = new ObterFederacaoHandler(new GestaoDeAcessoFalsa());

		var resultado = await handler.HandleAsync();

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Habilitada.Should().BeFalse();
		resultado.Value.DirectoryId.Should().BeNull();
	}

	[Fact]
	public async Task Definir_ComDirectoryIdValido_HabilitaEAudita()
	{
		var gestao = new GestaoDeAcessoFalsa();
		var trilha = new TrilhaDeAcessoFalsa();
		var handler = new DefinirFederacaoHandler(gestao, trilha);

		var resultado = await handler.HandleAsync(new DefinirFederacaoCommand(DirectoryId.ToString(), true));

		resultado.IsSuccess.Should().BeTrue();
		gestao.Federacao.Should().NotBeNull();
		gestao.Federacao!.DirectoryId.Should().Be(DirectoryId);
		gestao.Federacao.Habilitada.Should().BeTrue();
		trilha.Registros.Single().Verbo.Should().Be(Secco.Intranet.Application.Auditoria.VerbosDeAuditoria.AcessoFederacaoDefinir);
	}

	[Fact]
	public async Task Definir_Desabilitando_MantemODirectoryIdEnviado()
	{
		var gestao = new GestaoDeAcessoFalsa { Federacao = new FederacaoDto(DirectoryId, true, DateTimeOffset.UtcNow) };
		var handler = new DefinirFederacaoHandler(gestao, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(new DefinirFederacaoCommand(DirectoryId.ToString(), false));

		resultado.IsSuccess.Should().BeTrue();
		gestao.Federacao!.Habilitada.Should().BeFalse();
		gestao.Federacao.DirectoryId.Should().Be(DirectoryId);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("nao-e-um-guid")]
	[InlineData("00000000-0000-0000-0000-000000000000")]
	public async Task Definir_DirectoryIdInvalidoOuVazio_FalhaSemChamarAGestao(string? directoryId)
	{
		var gestao = new GestaoDeAcessoFalsa();
		var handler = new DefinirFederacaoHandler(gestao, new TrilhaDeAcessoFalsa());

		var resultado = await handler.HandleAsync(new DefinirFederacaoCommand(directoryId, true));

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(Secco.Intranet.Application.IntranetErrors.Acesso.DirectoryIdInvalido);
		gestao.Chamadas.Should().BeEmpty();
	}
}
