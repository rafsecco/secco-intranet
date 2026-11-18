using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Domain.Salas;
using Secco.Intranet.Tests.Support;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Casos de uso do cadastro de salas.</summary>
public class SalasHandlersTests
{
	private static readonly DateTimeOffset Agora = new(2027, 3, 15, 12, 0, 0, TimeSpan.Zero);

	private readonly SalaRepositorioFalso _repositorio = new();
	private readonly TrilhaDeAcessoFalsa _trilha = new();
	private readonly FakeTimeProvider _relogio = new(Agora);

	private static SalvarSalaCommand Comando(
		Guid? id = null,
		string? nome = "Sala Aurora",
		string? localizacao = "Andar 2",
		int? capacidade = 8,
		string? recursos = "TV",
		string? email = "aurora@exemplo.test",
		int ordem = 1,
		bool ativa = true) =>
		new(id, nome, localizacao, capacidade, recursos, email, ordem, ativa);

	private CriarSalaHandler Criar() => new(_repositorio, _trilha);

	private EditarSalaHandler Editar() => new(_repositorio, _trilha);

	private ExcluirSalaHandler Excluir() => new(_repositorio, _trilha, _relogio);

	private Sala Existente(string nome = "Sala Existente")
	{
		var sala = new Sala(nome, null, null, null, null, 0);
		_repositorio.Salas.Add(sala);

		return sala;
	}

	[Fact]
	public async Task Criar_Valida_Grava()
	{
		var resultado = await Criar().HandleAsync(Comando(nome: "  Sala Aurora  ", localizacao: " ", email: " aurora@exemplo.test "));

		resultado.IsSuccess.Should().BeTrue();
		var sala = _repositorio.Salas.Should().ContainSingle().Subject;
		sala.Id.Should().Be(resultado.Value);
		sala.Nome.Should().Be("Sala Aurora");
		sala.Localizacao.Should().BeNull();
		sala.EmailNaAgenda.Should().Be("aurora@exemplo.test");
		sala.Ativa.Should().BeTrue();
		var registro = _trilha.Registros.Should().ContainSingle().Subject;
		registro.Verbo.Should().Be("sala.criar");
		registro.Recurso.Should().Be(RecursosDeAuditoria.Sala);
		registro.RecursoId.Should().Be(sala.Id.ToString());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task Criar_NomeVazio_NomeRequired(string? nome)
	{
		var resultado = await Criar().HandleAsync(Comando(nome: nome));

		resultado.Error.Should().Be(IntranetErrors.Salas.NomeRequired);
		_repositorio.Salas.Should().BeEmpty();
	}

	[Fact]
	public async Task Criar_NomeAcimaDoLimite_NomeTooLong()
	{
		var resultado = await Criar().HandleAsync(Comando(nome: new string('a', Sala.NomeMaximo + 1)));

		resultado.Error.Should().Be(IntranetErrors.Salas.NomeTooLong(Sala.NomeMaximo));
	}

	[Fact]
	public async Task Criar_NomeEmUsoSemDiferenciarMaiusculas_NomeEmUso()
	{
		Existente("Sala Aurora");

		var resultado = await Criar().HandleAsync(Comando(nome: "sala aurora"));

		resultado.Error.Should().Be(IntranetErrors.Salas.NomeEmUso);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-3)]
	public async Task Criar_CapacidadeNaoPositiva_CapacidadeInvalida(int capacidade)
	{
		var resultado = await Criar().HandleAsync(Comando(capacidade: capacidade));

		resultado.Error.Should().Be(IntranetErrors.Salas.CapacidadeInvalida);
	}

	[Theory]
	[InlineData("sem-arroba")]
	[InlineData("Fulano <a@exemplo.test>")]
	[InlineData("a@")]
	public async Task Criar_EmailMalformado_EmailInvalido(string email)
	{
		var resultado = await Criar().HandleAsync(Comando(email: email));

		resultado.Error.Should().Be(IntranetErrors.Salas.EmailInvalido);
	}

	[Fact]
	public async Task Criar_LocalizacaoERecursosAcimaDoLimite_Erros()
	{
		(await Criar().HandleAsync(Comando(localizacao: new string('a', Sala.LocalizacaoMaxima + 1)))).Error
			.Should().Be(IntranetErrors.Salas.LocalizacaoTooLong(Sala.LocalizacaoMaxima));
		(await Criar().HandleAsync(Comando(recursos: new string('a', Sala.RecursosMaximo + 1)))).Error
			.Should().Be(IntranetErrors.Salas.RecursosTooLong(Sala.RecursosMaximo));
	}

	[Fact]
	public async Task Editar_SalaInexistente_NotFound()
	{
		var resultado = await Editar().HandleAsync(Comando(id: Guid.NewGuid()));

		resultado.Error.Should().Be(IntranetErrors.Salas.NotFound);
	}

	[Fact]
	public async Task Editar_NomeDeOutraSala_NomeEmUso_MasOProprioNomePassa()
	{
		Existente("Sala Outra");
		var sala = Existente("Sala Aurora");

		(await Editar().HandleAsync(Comando(id: sala.Id, nome: "SALA OUTRA"))).Error.Should().Be(IntranetErrors.Salas.NomeEmUso);
		(await Editar().HandleAsync(Comando(id: sala.Id, nome: "sala aurora"))).IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task Editar_Desativa()
	{
		var sala = Existente();

		var resultado = await Editar().HandleAsync(Comando(id: sala.Id, nome: "Sala Nova", ordem: 5, ativa: false));

		resultado.IsSuccess.Should().BeTrue();
		sala.Nome.Should().Be("Sala Nova");
		sala.Ordem.Should().Be(5);
		sala.Ativa.Should().BeFalse();
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be("sala.editar");

		await Editar().HandleAsync(Comando(id: sala.Id, nome: "Sala Nova", ativa: true));
		sala.Ativa.Should().BeTrue();
	}

	[Fact]
	public async Task Excluir_SalaInexistente_NotFound()
	{
		(await Excluir().HandleAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Salas.NotFound);
	}

	[Fact]
	public async Task Excluir_ComReservaFutura_TemReservaFutura()
	{
		var sala = Existente();
		_repositorio.Reservas.Add((sala.Id, Agora.AddHours(1)));

		var resultado = await Excluir().HandleAsync(sala.Id);

		resultado.Error.Should().Be(IntranetErrors.Salas.TemReservaFutura);
		_repositorio.Salas.Should().Contain(sala);
		_repositorio.Reservas.Should().HaveCount(1);
		_trilha.Registros.Should().BeEmpty();
	}

	[Fact]
	public async Task Excluir_SoComPassadas_ApagaTudo()
	{
		var sala = Existente();
		var outra = Existente("Sala Outra");
		_repositorio.Reservas.Add((sala.Id, Agora.AddHours(-1)));
		_repositorio.Reservas.Add((sala.Id, Agora));
		_repositorio.Reservas.Add((outra.Id, Agora.AddDays(1)));

		var resultado = await Excluir().HandleAsync(sala.Id);

		resultado.IsSuccess.Should().BeTrue();
		_repositorio.Salas.Should().ContainSingle().Which.Should().Be(outra);
		_repositorio.Reservas.Should().ContainSingle().Which.SalaId.Should().Be(outra.Id);
		_trilha.Registros.Should().ContainSingle().Which.Verbo.Should().Be("sala.excluir");
	}

	[Fact]
	public async Task Listar_E_Obter()
	{
		var ativa = Existente("Sala A");
		var inativa = Existente("Sala B");
		inativa.Desativar();

		(await new ListarSalasHandler(_repositorio).HandleAsync(somenteAtivas: false)).Should().HaveCount(2);
		(await new ListarSalasHandler(_repositorio).HandleAsync(somenteAtivas: true)).Should().ContainSingle().Which.Id.Should().Be(ativa.Id);

		var obtida = await new ObterSalaHandler(_repositorio).HandleAsync(inativa.Id);
		obtida.Value.Nome.Should().Be("Sala B");
		obtida.Value.Ativa.Should().BeFalse();
		(await new ObterSalaHandler(_repositorio).HandleAsync(Guid.NewGuid())).Error.Should().Be(IntranetErrors.Salas.NotFound);
	}
}
