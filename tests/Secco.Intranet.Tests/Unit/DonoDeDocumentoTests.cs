using AwesomeAssertions;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public sealed class DonoDeDocumentoTests
{
	private static Documento Arquivo(DonoDeDocumento dono) =>
		new(dono, "Manual", null, "manual.pdf", "application/pdf", 10, Visibilidade.Setor, "a/b", "chave", "ana");

	[Fact]
	public void Documento_de_biblioteca_nao_tem_setor()
	{
		var bibliotecaId = Guid.NewGuid();

		var documento = Arquivo(DonoDeDocumento.DaBiblioteca(bibliotecaId));

		documento.SetorId.Should().BeNull();
		documento.BibliotecaId.Should().Be(bibliotecaId);
		documento.Dono.EhValido.Should().BeTrue();
	}

	[Fact]
	public void Documento_de_setor_nao_tem_biblioteca()
	{
		var setorId = Guid.NewGuid();

		var documento = Arquivo(DonoDeDocumento.DoSetor(setorId));

		documento.SetorId.Should().Be(setorId);
		documento.BibliotecaId.Should().BeNull();
	}

	[Fact]
	public void Dono_sem_nenhum_id_e_recusado()
	{
		var acao = () => Arquivo(new DonoDeDocumento(null, null));

		acao.Should().Throw<DomainInvariantException>()
			.WithMessage("Um documento exige exatamente um dono: setor ou biblioteca.");
	}

	[Fact]
	public void Dono_com_os_dois_ids_e_recusado()
	{
		var acao = () => Arquivo(new DonoDeDocumento(Guid.NewGuid(), Guid.NewGuid()));

		acao.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Dono_com_guid_vazio_e_recusado()
	{
		var acao = () => DonoDeDocumento.DoSetor(Guid.Empty);
		var outra = () => DonoDeDocumento.DaBiblioteca(Guid.Empty);

		acao.Should().Throw<DomainInvariantException>();
		outra.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Grupo_de_biblioteca_grava_visibilidade_setor()
	{
		var bibliotecaId = Guid.NewGuid();

		var grupo = Documento.CriarGrupo(DonoDeDocumento.DaBiblioteca(bibliotecaId), "Pasta", null, "ana");

		grupo.BibliotecaId.Should().Be(bibliotecaId);
		grupo.SetorId.Should().BeNull();
		grupo.Visibilidade.Should().Be(Visibilidade.Setor);
	}
}
