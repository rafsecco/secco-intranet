using AwesomeAssertions;
using Secco.Intranet.Domain;
using Secco.Intranet.Domain.Documentos;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Invariantes de tipo, pai e ordem do <see cref="Documento"/>.</summary>
public class DocumentoHierarquiaTests
{
	[Fact]
	public void CriarGrupo_SemArquivo_TipoGrupoEVisibilidadeSetor()
	{
		var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(Guid.NewGuid()), "Políticas", "Tudo sobre políticas", "quem.criou");

		grupo.Tipo.Should().Be(TipoDeDocumento.Grupo);
		grupo.EhGrupo.Should().BeTrue();
		grupo.NomeArquivo.Should().BeNull();
		grupo.ContentType.Should().BeNull();
		grupo.CaminhoRelativo.Should().BeNull();
		grupo.ChaveEmbrulhada.Should().BeNull();
		grupo.Tamanho.Should().BeNull();
		grupo.Visibilidade.Should().Be(Visibilidade.Setor);
		grupo.Ativo.Should().BeTrue();
	}

	[Fact]
	public void CriarGrupo_TituloVazio_Lanca()
	{
		var criar = () => Documento.CriarGrupo(DonoDeDocumento.DoSetor(Guid.NewGuid()), "  ", null, "quem.criou");

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Arquivo_ContinuaExigindoArquivo()
	{
		var criar = () => new Documento(
			DonoDeDocumento.DoSetor(Guid.NewGuid()),
			"Política",
			null,
			"politica.pdf",
			"application/pdf",
			128,
			Visibilidade.Setor,
			" ",
			"secco-enc:v1:x",
			"quem.criou");

		criar.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Posicionar_TrocaPaiEOrdem()
	{
		var grupo = Documento.CriarGrupo(DonoDeDocumento.DoSetor(Guid.NewGuid()), "Políticas", null, "quem.criou");
		var pai = Guid.NewGuid();

		grupo.Posicionar(pai, 3);

		grupo.ParentId.Should().Be(pai);
		grupo.Ordem.Should().Be(3);
	}
}
