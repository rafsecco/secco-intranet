using AwesomeAssertions;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class ListarArvoresDosSetoresHandlerTests
{
	[Fact]
	public async Task AgrupaPorSetor_SoComItensAtivos()
	{
		var repo = new ItemMenuRepositorioFalso();
		var financeiro = Guid.NewGuid();
		var ti = Guid.NewGuid();
		var raizF = new ItemMenu(financeiro, null, "Financeiro", "financeiro", TipoDeItemMenu.Setor, null, null, 0);
		var docsF = new ItemMenu(financeiro, raizF.Id, "Documentos", "documentos", TipoDeItemMenu.Documentos, null, null, 0);
		var avisosF = new ItemMenu(financeiro, raizF.Id, "Avisos", "avisos", TipoDeItemMenu.Avisos, null, null, 1);
		avisosF.Desativar();
		var raizT = new ItemMenu(ti, null, "TI", "ti", TipoDeItemMenu.Setor, null, null, 0);
		repo.Itens.AddRange([raizF, docsF, avisosF, raizT]);

		var arvores = await new ListarArvoresDosSetoresHandler(repo).HandleAsync([financeiro, ti]);

		arvores[financeiro].Select(item => item.Id).Should().BeEquivalentTo([raizF.Id, docsF.Id]);
		arvores[ti].Should().ContainSingle(item => item.Id == raizT.Id);
	}
}
