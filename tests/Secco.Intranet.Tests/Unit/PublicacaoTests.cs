using AwesomeAssertions;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Publicacoes;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Invariantes do agregado. O estado "no ar" nasce aqui como pergunta ao relógio, e não
/// como coluna — é a decisão que dispensa job de expiração.
/// </summary>
public class PublicacaoTests
{
	private static readonly DateTimeOffset Agora = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

	private static Publicacao Criar(
		string titulo = "Recesso de fim de ano",
		string corpo = "O recesso vai de 23/12 a 02/01.",
		DateTimeOffset? publicadoEm = null,
		DateTimeOffset? expiraEm = null) =>
		new(Guid.NewGuid(), titulo, corpo, TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, publicadoEm ?? Agora, expiraEm, "teste");

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Construtor_SemTitulo_Recusa(string titulo)
	{
		var criar = () => Criar(titulo: titulo);

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_SemCorpo_Recusa()
	{
		var criar = () => Criar(corpo: "   ");

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_SemSetor_Recusa()
	{
		var criar = () => new Publicacao(Guid.Empty, "T", "C", TipoPublicacao.Aviso,
			Visibilidade.Empresa, PrioridadePublicacao.Normal, Agora, null, "teste");

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Construtor_ComExpiracaoAntesDaEntradaNoAr_Recusa()
	{
		var criar = () => Criar(publicadoEm: Agora, expiraEm: Agora.AddMinutes(-1));

		criar.Should().Throw<DomainInvariantException>(
			"uma publicação que expira antes de nascer é erro de digitação, não regra de negócio");
	}

	[Fact]
	public void Construtor_ComExpiracaoIgualAEntradaNoAr_Recusa()
	{
		var criar = () => Criar(publicadoEm: Agora, expiraEm: Agora);

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void EstaNoAr_Publicada_Sim() =>
		Criar(publicadoEm: Agora.AddHours(-1)).EstaNoAr(Agora).Should().BeTrue();

	[Fact]
	public void EstaNoAr_Agendada_Nao() =>
		Criar(publicadoEm: Agora.AddHours(1)).EstaNoAr(Agora).Should().BeFalse();

	[Fact]
	public void EstaNoAr_Expirada_Nao() =>
		Criar(publicadoEm: Agora.AddHours(-2), expiraEm: Agora.AddHours(-1))
			.EstaNoAr(Agora).Should().BeFalse();

	[Fact]
	public void EstaNoAr_ExpirandoExatamenteAgora_Nao() =>
		Criar(publicadoEm: Agora.AddHours(-2), expiraEm: Agora)
			.EstaNoAr(Agora).Should().BeFalse("a comparação é > e não >=");

	[Fact]
	public void EstaNoAr_Arquivada_Nao()
	{
		var publicacao = Criar(publicadoEm: Agora.AddHours(-1));

		publicacao.Arquivar();

		publicacao.EstaNoAr(Agora).Should().BeFalse();
	}

	[Fact]
	public void Editar_ComExpiracaoInvalida_Recusa()
	{
		var publicacao = Criar();

		var editar = () => publicacao.Editar("T", "C", TipoPublicacao.Aviso, Visibilidade.Empresa,
			PrioridadePublicacao.Normal, Agora, Agora.AddMinutes(-1));

		editar.Should().Throw<DomainInvariantException>("editar revalida as mesmas invariantes");
	}

	[Fact]
	public void Editar_CarimbaAtualizadoEm()
	{
		var publicacao = Criar();

		publicacao.Editar("Novo título", "Novo corpo", TipoPublicacao.Noticia, Visibilidade.Setor,
			PrioridadePublicacao.Importante, Agora, null);

		publicacao.AtualizadoEm.Should().NotBeNull();
		publicacao.Titulo.Should().Be("Novo título");
		publicacao.Visibilidade.Should().Be(Visibilidade.Setor);
	}
}
