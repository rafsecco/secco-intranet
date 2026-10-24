using AwesomeAssertions;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Application.Vencimentos;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Regras puras do aviso de vencimento: canais, destinatários, lotes e título.</summary>
public class AvisoDeVencimentoTests
{
	private const string Slug = "ti";

	private static UsuarioDoTenant Usuario(string? email, params string[] roles) =>
		new(Guid.NewGuid(), email, roles);

	[Fact]
	public void Canais_MaisDeSeteDias_SoSino() =>
		AvisoDeVencimento.Canais(8).Should().Equal("in_app");

	[Theory]
	[InlineData(7)]
	[InlineData(1)]
	[InlineData(0)]
	public void Canais_SeteDiasOuMenos_SinoEEmail(int dias) =>
		AvisoDeVencimento.Canais(dias).Should().Equal("in_app", "email");

	[Fact]
	public void Origem_ETipo_SaoOsDoContrato()
	{
		AvisoDeVencimento.Origem.Should().Be("vencimentos");
		AvisoDeVencimento.Tipo.Should().Be("aviso");
	}

	[Fact]
	public void Destinatarios_ResponsavelMaisAdmins_SemRepeticao()
	{
		var responsavel = Usuario("r@x.com", "ti-admin");
		var outroAdmin = Usuario("a@x.com", "ti-admin");
		var user = Usuario("u@x.com", "ti-user");

		var resultado = AvisoDeVencimento.Destinatarios([responsavel, outroAdmin, user], Slug, responsavel.Id);

		resultado.Select(u => u.Id).Should().BeEquivalentTo([responsavel.Id, outroAdmin.Id]);
	}

	[Fact]
	public void Destinatarios_ResponsavelComRoleUser_Recebe()
	{
		var responsavel = Usuario("r@x.com", "ti-user");
		var admin = Usuario("a@x.com", "ti-admin");

		AvisoDeVencimento.Destinatarios([responsavel, admin], Slug, responsavel.Id)
			.Select(u => u.Id).Should().BeEquivalentTo([responsavel.Id, admin.Id]);
	}

	[Fact]
	public void ResponsavelSemRoleDoSetor_NaoRecebe()
	{
		var responsavel = Usuario("r@x.com", "rh-admin");
		var admin = Usuario("a@x.com", "ti-admin");

		AvisoDeVencimento.Destinatarios([responsavel, admin], Slug, responsavel.Id)
			.Select(u => u.Id).Should().Equal(admin.Id);
	}

	[Fact]
	public void UsuarioComRoleUser_SemSerResponsavel_NaoRecebe()
	{
		var user = Usuario("u@x.com", "ti-user");
		var admin = Usuario("a@x.com", "ti-admin");

		AvisoDeVencimento.Destinatarios([user, admin], Slug, null)
			.Select(u => u.Id).Should().Equal(admin.Id);
	}

	[Fact]
	public void Lotes_SemEmail_RecebeSoNoSino()
	{
		var comEmail = Usuario("a@x.com", "ti-admin");
		var semEmail = Usuario(null, "ti-admin");

		var lotes = AvisoDeVencimento.Lotes([comEmail, semEmail], ["in_app", "email"]);

		lotes.Should().HaveCount(2);
		lotes[0].Canais.Should().Equal("in_app", "email");
		lotes[0].Destinos.Should().Equal(new DestinoDaMensagem(comEmail.Id, "a@x.com"));
		lotes[1].Canais.Should().Equal("in_app");
		lotes[1].Destinos.Should().Equal(new DestinoDaMensagem(semEmail.Id, null));
	}

	[Fact]
	public void Lotes_SoSino_UmLoteSemEmailNosDestinos()
	{
		var usuario = Usuario("a@x.com", "ti-admin");

		var lotes = AvisoDeVencimento.Lotes([usuario], ["in_app"]);

		lotes.Should().ContainSingle();
		lotes[0].Canais.Should().Equal("in_app");
		lotes[0].Destinos.Should().Equal(new DestinoDaMensagem(usuario.Id, null));
	}

	[Fact]
	public void Lotes_LoteVazio_NaoSai()
	{
		var lotes = AvisoDeVencimento.Lotes([Usuario("a@x.com", "ti-admin")], ["in_app", "email"]);

		lotes.Should().ContainSingle();
	}

	[Fact]
	public void Lotes_FatiaraEm500()
	{
		var usuarios = Enumerable.Range(0, 1001).Select(i => Usuario($"u{i}@x.com", "ti-admin")).ToList();

		var lotes = AvisoDeVencimento.Lotes(usuarios, ["in_app", "email"]);

		lotes.Select(l => l.Destinos.Count).Should().Equal(500, 500, 1);
	}

	[Theory]
	[InlineData(5, "Certificado A1 vence em 5 dias")]
	[InlineData(1, "Certificado A1 vence em 1 dia")]
	[InlineData(0, "Certificado A1 vence hoje")]
	public void Titulo_UsaOsDiasReais(int dias, string esperado) =>
		AvisoDeVencimento.Titulo("Certificado A1", dias).Should().Be(esperado);
}
