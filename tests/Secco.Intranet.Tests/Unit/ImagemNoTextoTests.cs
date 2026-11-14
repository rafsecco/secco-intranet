using AwesomeAssertions;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Invariantes da imagem inserida no meio do texto de uma publicação.</summary>
public class ImagemNoTextoTests
{
	[Fact]
	public void Construtor_DadosValidos_GuardaOsValores()
	{
		var publicacaoId = Guid.NewGuid();

		var imagem = new ImagemNoTexto(publicacaoId, "publicacoes/a/b.webp", "chave");

		imagem.PublicacaoId.Should().Be(publicacaoId);
		imagem.CaminhoRelativo.Should().Be("publicacoes/a/b.webp");
		imagem.ChaveEmbrulhada.Should().Be("chave");
		imagem.Id.Should().NotBeEmpty();
	}

	[Fact]
	public void Construtor_PublicacaoVazia_Recusa()
	{
		var criar = () => new ImagemNoTexto(Guid.Empty, "caminho", "chave");

		criar.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Construtor_CaminhoVazio_Recusa(string caminho)
	{
		var criar = () => new ImagemNoTexto(Guid.NewGuid(), caminho, "chave");

		criar.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Construtor_ChaveVazia_Recusa(string chave)
	{
		var criar = () => new ImagemNoTexto(Guid.NewGuid(), "caminho", chave);

		criar.Should().Throw<DomainInvariantException>();
	}
}
