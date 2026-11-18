using System.Net.Mail;
using System.Text.Json;
using Secco.Intranet.Domain.Salas;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Salas;

/// <summary>Sala de reunião como a interface a enxerga.</summary>
public sealed record SalaDto(
	Guid Id,
	string Nome,
	string? Localizacao,
	int? Capacidade,
	string? Recursos,
	string? EmailNaAgenda,
	int Ordem,
	bool Ativa)
{
	/// <summary>Converte a entidade.</summary>
	public static SalaDto De(Sala sala)
	{
		ArgumentNullException.ThrowIfNull(sala);

		return new SalaDto(
			sala.Id, sala.Nome, sala.Localizacao, sala.Capacidade, sala.Recursos, sala.EmailNaAgenda, sala.Ordem, sala.Ativa);
	}
}

/// <summary>Pedido de criação ou edição de sala.</summary>
/// <param name="Id">Sala editada; <c>null</c> ao criar.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Localizacao">Onde fica.</param>
/// <param name="Capacidade">Número de lugares.</param>
/// <param name="Recursos">Recursos disponíveis.</param>
/// <param name="EmailNaAgenda">E-mail do recurso da sala na agenda externa.</param>
/// <param name="Ordem">Posição na listagem.</param>
/// <param name="Ativa">Se a sala aceita novas reservas.</param>
public sealed record SalvarSalaCommand(
	Guid? Id,
	string? Nome,
	string? Localizacao,
	int? Capacidade,
	string? Recursos,
	string? EmailNaAgenda,
	int Ordem,
	bool Ativa);

/// <summary>Dados da sala já aparados e validados.</summary>
internal sealed record DadosDaSala(
	string Nome, string? Localizacao, int? Capacidade, string? Recursos, string? EmailNaAgenda, int Ordem);

/// <summary>Validação e metadados de auditoria compartilhados pelos handlers de sala.</summary>
internal static class ApoioDeSalas
{
	/// <summary>Apara os textos (vazio opcional vira <c>null</c>) e valida o pedido.</summary>
	public static Result<DadosDaSala> Validar(SalvarSalaCommand command)
	{
		var nome = command.Nome?.Trim();

		if (string.IsNullOrEmpty(nome))
		{
			return IntranetErrors.Salas.NomeRequired;
		}

		if (nome.Length > Sala.NomeMaximo)
		{
			return IntranetErrors.Salas.NomeTooLong(Sala.NomeMaximo);
		}

		var localizacao = Aparar(command.Localizacao);

		if (localizacao is { Length: > Sala.LocalizacaoMaxima })
		{
			return IntranetErrors.Salas.LocalizacaoTooLong(Sala.LocalizacaoMaxima);
		}

		if (command.Capacidade is <= 0)
		{
			return IntranetErrors.Salas.CapacidadeInvalida;
		}

		var recursos = Aparar(command.Recursos);

		if (recursos is { Length: > Sala.RecursosMaximo })
		{
			return IntranetErrors.Salas.RecursosTooLong(Sala.RecursosMaximo);
		}

		var email = Aparar(command.EmailNaAgenda);

		if (email is not null && !EmailValido(email))
		{
			return IntranetErrors.Salas.EmailInvalido;
		}

		return new DadosDaSala(nome, localizacao, command.Capacidade, recursos, email, command.Ordem);
	}

	/// <summary>Metadados da trilha: o que mudou de importante, sem o e-mail.</summary>
	public static string Metadata(Sala sala) =>
		JsonSerializer.Serialize(new Dictionary<string, object?>
		{
			["nome"] = sala.Nome,
			["ativa"] = sala.Ativa,
		});

	private static string? Aparar(string? texto)
	{
		var aparado = texto?.Trim();

		return string.IsNullOrEmpty(aparado) ? null : aparado;
	}

	private static bool EmailValido(string email) =>
		email.Length <= Sala.EmailMaximo
		&& MailAddress.TryCreate(email, out var endereco)
		&& string.Equals(endereco.Address, email, StringComparison.Ordinal);
}
