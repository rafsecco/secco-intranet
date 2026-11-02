using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Edição de biblioteca e os verbos da trilha. O slug não muda: é a base das permissões.</summary>
public class EditarBibliotecaHandlerTests
{
	private readonly TrilhaDeAcessoFalsa _trilha = new();

	private EditarBibliotecaHandler Montar(Biblioteca biblioteca) =>
		new(new BibliotecaRepositorioFalso(biblioteca), new IntranetOptions(), _trilha);

	[Fact]
	public async Task Renomear_grava_biblioteca_editar_e_nao_muda_o_slug()
	{
		var biblioteca = new Biblioteca("Manuais", "manuais");

		var resultado = await Montar(biblioteca)
			.HandleAsync(new EditarBibliotecaCommand(biblioteca.Id, "Manuais Técnicos", "bi-book", true));

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Nome.Should().Be("Manuais Técnicos");
		resultado.Value.Icone.Should().Be("bi-book");
		resultado.Value.Slug.Should().Be("manuais");
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.BibliotecaEditar);
	}

	[Fact]
	public async Task Ativo_falso_grava_biblioteca_desativar()
	{
		var biblioteca = new Biblioteca("Manuais", "manuais");

		var resultado = await Montar(biblioteca)
			.HandleAsync(new EditarBibliotecaCommand(biblioteca.Id, "Manuais", null, false));

		resultado.Value.Ativo.Should().BeFalse();
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.BibliotecaDesativar);
	}

	[Fact]
	public async Task Ativo_verdadeiro_numa_inativa_grava_biblioteca_reativar()
	{
		var biblioteca = new Biblioteca("Manuais", "manuais");
		biblioteca.Desativar();

		var resultado = await Montar(biblioteca)
			.HandleAsync(new EditarBibliotecaCommand(biblioteca.Id, "Manuais", null, true));

		resultado.Value.Ativo.Should().BeTrue();
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be(VerbosDeAuditoria.BibliotecaReativar);
	}

	[Fact]
	public async Task Inexistente_devolve_not_found_sem_trilha()
	{
		var resultado = await Montar(new Biblioteca("Manuais", "manuais"))
			.HandleAsync(new EditarBibliotecaCommand(Guid.NewGuid(), "Manuais", null, true));

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.NotFound);
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Nome_vazio_e_icone_invalido_recusam()
	{
		var biblioteca = new Biblioteca("Manuais", "manuais");
		var handler = Montar(biblioteca);

		(await handler.HandleAsync(new EditarBibliotecaCommand(biblioteca.Id, " ", null, true)))
			.Error.Should().Be(IntranetErrors.Bibliotecas.NomeRequired);
		(await handler.HandleAsync(new EditarBibliotecaCommand(biblioteca.Id, "Manuais", "fa fa-x", true)))
			.Error.Should().Be(IntranetErrors.Bibliotecas.IconeInvalido);
		biblioteca.Nome.Should().Be("Manuais");
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Obter_por_slug_so_devolve_biblioteca_ativa()
	{
		var ativa = new Biblioteca("Manuais", "manuais");
		var inativa = new Biblioteca("Velha", "velha");
		inativa.Desativar();
		var handler = new ObterBibliotecaPorSlugHandler(new BibliotecaRepositorioFalso(ativa, inativa));

		(await handler.HandleAsync("manuais")).IsSuccess.Should().BeTrue();
		(await handler.HandleAsync("velha")).Error.Should().Be(IntranetErrors.Bibliotecas.NotFound);
		(await handler.HandleAsync("nao-existe")).Error.Should().Be(IntranetErrors.Bibliotecas.NotFound);
	}
}
