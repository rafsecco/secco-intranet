using System.Text.Json;
using AwesomeAssertions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Bibliotecas;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.Intranet.Tests.Support;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>
/// Criação de biblioteca: tudo é validado antes de o SecureGate ser chamado, e o SecureGate
/// é chamado antes de gravar (ADR-0001).
/// </summary>
public class CriarBibliotecaHandlerTests
{
	private readonly GestaoDeAcessoFalsa _gestao = new GestaoDeAcessoFalsa().ComPerfilImplicito();
	private readonly TrilhaDeAcessoFalsa _trilha = new();

	private static CriarBibliotecaCommand Comando(
		string? nome = "Manuais", string? slug = "manuais", string? icone = null, bool todosLeem = true) =>
		new(nome, slug, icone, todosLeem);

	private (CriarBibliotecaHandler Handler, BibliotecaRepositorioFalso Repositorio) Montar(
		params Biblioteca[] existentes)
	{
		var repositorio = new BibliotecaRepositorioFalso(existentes);
		var handler = new CriarBibliotecaHandler(
			repositorio, new IntranetOptions(), _gestao, _trilha, new SlugsReservadosDeBibliotecaFalsos("nova"));

		return (handler, repositorio);
	}

	[Fact]
	public async Task Criar_com_todos_leem_garante_read_no_tenant_member_antes_de_gravar()
	{
		var (handler, repositorio) = Montar();

		var resultado = await handler.HandleAsync(Comando());

		resultado.IsSuccess.Should().BeTrue();
		resultado.Value.Slug.Should().Be("manuais");
		_gestao.Chamadas.Should().ContainSingle()
			.Which.Should().Be("permissoes-garantir:tenant-member:biblioteca-manuais:read");
		(await repositorio.GetBySlugAsync("manuais")).Should().NotBeNull();
	}

	[Fact]
	public async Task Criar_com_falha_no_securegate_nao_grava()
	{
		_gestao.FalharCom = IntranetErrors.Acesso.PermissaoInvalida;
		var (handler, repositorio) = Montar();

		var resultado = await handler.HandleAsync(Comando());

		resultado.IsFailure.Should().BeTrue();
		resultado.Error.Should().Be(IntranetErrors.Acesso.PermissaoInvalida);
		(await repositorio.ListarAsync(false)).Should().BeEmpty();
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_sem_todos_leem_nao_chama_o_securegate()
	{
		var (handler, repositorio) = Montar();

		var resultado = await handler.HandleAsync(Comando(todosLeem: false));

		resultado.IsSuccess.Should().BeTrue();
		_gestao.Chamadas.Should().BeEmpty();
		(await repositorio.ListarAsync(false)).Should().ContainSingle();
	}

	[Fact]
	public async Task Criar_normaliza_o_slug_para_minusculo()
	{
		var (handler, _) = Montar();

		var resultado = await handler.HandleAsync(Comando(slug: " Manuais "));

		resultado.Value.Slug.Should().Be("manuais");
		_gestao.Chamadas.Single().Should().EndWith("biblioteca-manuais:read");
	}

	[Fact]
	public async Task Criar_com_slug_repetido_recusa_sem_chamar_o_securegate()
	{
		var (handler, _) = Montar(new Biblioteca("Manuais", "manuais"));

		var resultado = await handler.HandleAsync(Comando());

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.SlugAlreadyExists("manuais"));
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_com_slug_reservado_recusa_sem_chamar_o_securegate()
	{
		var (handler, repositorio) = Montar();

		var resultado = await handler.HandleAsync(Comando(slug: "nova"));

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.SlugReservado("nova"));
		_gestao.Chamadas.Should().BeEmpty();
		(await repositorio.ListarAsync(false)).Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_com_slug_de_54_caracteres_recusa_sem_chamar_o_securegate()
	{
		var (handler, _) = Montar();

		var resultado = await handler.HandleAsync(Comando(slug: new string('a', 54)));

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.SlugInvalido);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_com_slug_vazio_recusa()
	{
		var (handler, _) = Montar();

		var resultado = await handler.HandleAsync(Comando(slug: " "));

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.SlugRequired);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_com_icone_invalido_recusa_sem_chamar_o_securegate()
	{
		var (handler, _) = Montar();

		var resultado = await handler.HandleAsync(Comando(icone: "fa fa-x"));

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.IconeInvalido);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_com_nome_vazio_recusa_sem_chamar_o_securegate()
	{
		var (handler, _) = Montar();

		var resultado = await handler.HandleAsync(Comando(nome: " "));

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.NomeRequired);
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_com_nome_longo_demais_recusa()
	{
		var (handler, _) = Montar();
		var limite = new IntranetOptions().MaxNameLength;

		var resultado = await handler.HandleAsync(Comando(nome: new string('n', limite + 1)));

		resultado.Error.Should().Be(IntranetErrors.Bibliotecas.NomeTooLong(limite));
		_gestao.Chamadas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_registra_biblioteca_criar_com_todosLeem()
	{
		var (handler, _) = Montar();

		var resultado = await handler.HandleAsync(Comando(todosLeem: true));

		var registro = _trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be(VerbosDeAuditoria.BibliotecaCriar);
		registro.Recurso.Should().Be(RecursosDeAuditoria.Biblioteca);
		registro.RecursoId.Should().Be(resultado.Value.Id.ToString());
		using var metadata = JsonDocument.Parse(registro.Metadata!);
		metadata.RootElement.GetProperty("slug").GetString().Should().Be("manuais");
		metadata.RootElement.GetProperty("todosLeem").GetBoolean().Should().BeTrue();
	}
}
