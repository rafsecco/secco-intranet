using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Salas;

/// <summary>
/// Sala de reunião reservável. Desativar uma sala a tira da escolha de novas reservas,
/// sem apagar o histórico.
/// </summary>
public sealed class Sala : BaseEntity
{
	/// <summary>Tamanho máximo do nome.</summary>
	public const int NomeMaximo = 120;

	/// <summary>Tamanho máximo da localização.</summary>
	public const int LocalizacaoMaxima = 200;

	/// <summary>Tamanho máximo da descrição dos recursos.</summary>
	public const int RecursosMaximo = 500;

	/// <summary>Tamanho máximo do e-mail da sala na agenda.</summary>
	public const int EmailMaximo = 256;

	private Sala()
	{
		// Construtor de rehidratação do EF Core
		Nome = string.Empty;
	}

	/// <summary>Cria uma sala ativa.</summary>
	/// <param name="nome">Nome de exibição. Obrigatório.</param>
	/// <param name="localizacao">Onde fica (andar, bloco...). Opcional.</param>
	/// <param name="capacidade">Número de lugares; se informado, maior que zero.</param>
	/// <param name="recursos">Recursos disponíveis (TV, projetor...). Opcional.</param>
	/// <param name="emailNaAgenda">E-mail do recurso da sala na agenda externa. Opcional.</param>
	/// <param name="ordem">Posição na listagem.</param>
	/// <exception cref="DomainInvariantException">Nome vazio ou longo demais, capacidade inválida ou texto acima do limite.</exception>
	public Sala(string nome, string? localizacao, int? capacidade, string? recursos, string? emailNaAgenda, int ordem)
	{
		Validar(nome, localizacao, capacidade, recursos, emailNaAgenda);

		Nome = nome;
		Localizacao = localizacao;
		Capacidade = capacidade;
		Recursos = recursos;
		EmailNaAgenda = emailNaAgenda;
		Ordem = ordem;
		Ativa = true;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Nome de exibição (coluna <c>ds_nome</c>)</summary>
	public string Nome { get; private set; }

	/// <summary>Onde a sala fica (coluna <c>ds_localizacao</c>)</summary>
	public string? Localizacao { get; private set; }

	/// <summary>Número de lugares (coluna <c>nr_capacidade</c>)</summary>
	public int? Capacidade { get; private set; }

	/// <summary>Recursos disponíveis (coluna <c>ds_recursos</c>)</summary>
	public string? Recursos { get; private set; }

	/// <summary>E-mail do recurso da sala na agenda externa (coluna <c>ds_email_na_agenda</c>)</summary>
	public string? EmailNaAgenda { get; private set; }

	/// <summary>Posição na listagem (coluna <c>nr_ordem</c>)</summary>
	public int Ordem { get; private set; }

	/// <summary>Indica se a sala aceita novas reservas (coluna <c>fl_ativa</c>)</summary>
	public bool Ativa { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>)</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Altera os dados cadastrais da sala.</summary>
	/// <exception cref="DomainInvariantException">Mesmas regras do construtor.</exception>
	public void Editar(string nome, string? localizacao, int? capacidade, string? recursos, string? emailNaAgenda, int ordem)
	{
		Validar(nome, localizacao, capacidade, recursos, emailNaAgenda);

		Nome = nome;
		Localizacao = localizacao;
		Capacidade = capacidade;
		Recursos = recursos;
		EmailNaAgenda = emailNaAgenda;
		Ordem = ordem;
	}

	/// <summary>Volta a aceitar novas reservas.</summary>
	public void Ativar() => Ativa = true;

	/// <summary>Deixa de aceitar novas reservas.</summary>
	public void Desativar() => Ativa = false;

	private static void Validar(string nome, string? localizacao, int? capacidade, string? recursos, string? emailNaAgenda)
	{
		if (string.IsNullOrWhiteSpace(nome))
		{
			throw new DomainInvariantException("Uma sala exige nome não vazio.");
		}

		if (nome.Length > NomeMaximo)
		{
			throw new DomainInvariantException($"O nome da sala aceita até {NomeMaximo} caracteres.");
		}

		if (localizacao is { Length: > LocalizacaoMaxima })
		{
			throw new DomainInvariantException($"A localização da sala aceita até {LocalizacaoMaxima} caracteres.");
		}

		if (capacidade is <= 0)
		{
			throw new DomainInvariantException("A capacidade da sala deve ser maior que zero.");
		}

		if (recursos is { Length: > RecursosMaximo })
		{
			throw new DomainInvariantException($"Os recursos da sala aceitam até {RecursosMaximo} caracteres.");
		}

		if (emailNaAgenda is { Length: > EmailMaximo })
		{
			throw new DomainInvariantException($"O e-mail da sala aceita até {EmailMaximo} caracteres.");
		}
	}
}
