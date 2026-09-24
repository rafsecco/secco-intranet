using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ObterPessoaParaEdicaoHandlerTests
{
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();

	[Fact]
	public async Task TrazSetoresAtivosEOsPossiveisGestoresSemAPropriaPessoa()
	{
		var usuarios = new UsuariosParaDiretorioFalso().Com(Ana, "ana@x.com").Com(Bruno, "bruno@x.com");
		var setores = new SetoresFalsos();
		setores.Com("Ativo", "ativo");
		setores.Com("Inativo", "inativo", ativo: false);

		var resultado = await new ObterPessoaParaEdicaoHandler(usuarios, new PerfisColaboradorFalso(), setores).HandleAsync(Ana);

		resultado.Value.Pessoa.UsuarioId.Should().Be(Ana);
		resultado.Value.SetoresAtivos.Select(s => s.Slug).Should().Equal("ativo");
		resultado.Value.GestoresPossiveis.Select(p => p.UsuarioId).Should().Equal(Bruno);
	}

	[Fact]
	public async Task PessoaInexistente_NotFound()
	{
		var resultado = await new ObterPessoaParaEdicaoHandler(new UsuariosParaDiretorioFalso(), new PerfisColaboradorFalso(), new SetoresFalsos())
			.HandleAsync(Ana);

		resultado.Error.Should().Be(IntranetErrors.Diretorio.PessoaNaoEncontrada);
	}
}
