using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Salas;

/// <summary>
/// Reserva de uma sala por um intervalo, com convidados e, opcionalmente, espelhamento na agenda externa.
/// "Mesmo dia local" e "não no passado" dependem de fuso e relógio e são verificados pela aplicação.
/// </summary>
public sealed class Reserva : BaseEntity
{
	/// <summary>Tamanho máximo do título.</summary>
	public const int TituloMaximo = 120;

	/// <summary>Tamanho máximo da descrição.</summary>
	public const int DescricaoMaxima = 1000;

	/// <summary>Máximo de convidados (sem contar o dono).</summary>
	public const int MaximoDeConvidados = 50;

	/// <summary>Passo, em minutos, dos horários de início e fim.</summary>
	public const int PassoEmMinutos = 15;

	private readonly List<ConvidadoDaReserva> _convidados = [];

	private Reserva()
	{
		// Construtor de rehidratação do EF Core
		Titulo = string.Empty;
		ReservadoPorNome = string.Empty;
	}

	/// <summary>Cria uma reserva na versão 1.</summary>
	/// <param name="salaId">Sala reservada.</param>
	/// <param name="inicio">Início, com minutos múltiplos de 15 e segundos zerados.</param>
	/// <param name="fim">Fim, posterior ao início, com a mesma regra de minutos.</param>
	/// <param name="titulo">Título. Obrigatório.</param>
	/// <param name="descricao">Descrição livre. Opcional.</param>
	/// <param name="reservadoPorId">Usuário dono da reserva.</param>
	/// <param name="reservadoPorNome">Nome do dono no momento da reserva.</param>
	/// <param name="reservadoPorEmail">E-mail do dono no momento da reserva. Opcional.</param>
	/// <param name="convidados">Convidados; o dono, se vier na lista, é retirado em silêncio.</param>
	/// <param name="espelhar">Se a reserva deve ser espelhada na agenda externa.</param>
	/// <exception cref="DomainInvariantException">Horário, título, descrição ou convidados inválidos.</exception>
	public Reserva(
		Guid salaId,
		DateTimeOffset inicio,
		DateTimeOffset fim,
		string titulo,
		string? descricao,
		Guid reservadoPorId,
		string reservadoPorNome,
		string? reservadoPorEmail,
		IEnumerable<ConvidadoDaReserva> convidados,
		bool espelhar)
	{
		Titulo = string.Empty; // validado e atribuído em AplicarDados
		ReservadoPorId = reservadoPorId;
		ReservadoPorNome = reservadoPorNome;
		ReservadoPorEmail = reservadoPorEmail;
		CreatedAt = DateTimeOffset.UtcNow;
		Versao = 1;

		AplicarDados(salaId, inicio, fim, titulo, descricao, convidados);

		Espelhamento = espelhar ? EstadoDoEspelhamento.Aguardando : EstadoDoEspelhamento.NaoSeAplica;
	}

	/// <summary>Sala reservada (coluna <c>id_fk_sala</c>)</summary>
	public Guid SalaId { get; private set; }

	/// <summary>Início da reserva (coluna <c>dt_inicio</c>)</summary>
	public DateTimeOffset Inicio { get; private set; }

	/// <summary>Fim da reserva (coluna <c>dt_fim</c>)</summary>
	public DateTimeOffset Fim { get; private set; }

	/// <summary>Título (coluna <c>ds_titulo</c>)</summary>
	public string Titulo { get; private set; }

	/// <summary>Descrição livre (coluna <c>ds_descricao</c>)</summary>
	public string? Descricao { get; private set; }

	/// <summary>Usuário dono da reserva (coluna <c>reservado_por_id</c>, sem FK: a identidade vive no SecureGate)</summary>
	public Guid ReservadoPorId { get; private set; }

	/// <summary>Nome do dono no momento da reserva (coluna <c>ds_reservado_por_nome</c>)</summary>
	public string ReservadoPorNome { get; private set; }

	/// <summary>E-mail do dono no momento da reserva (coluna <c>ds_reservado_por_email</c>)</summary>
	public string? ReservadoPorEmail { get; private set; }

	/// <summary>Convidados, sem o dono e sem repetição.</summary>
	public IReadOnlyList<ConvidadoDaReserva> Convidados => _convidados;

	/// <summary>Versão da reserva; nasce em 1 e sobe a cada <see cref="Alterar"/> (coluna <c>nr_versao</c>)</summary>
	public int Versao { get; private set; }

	/// <summary>Situação do espelhamento na agenda externa (coluna <c>ie_espelhamento</c>)</summary>
	public EstadoDoEspelhamento Espelhamento { get; private set; }

	/// <summary>Identificador do evento na agenda externa, quando já criado (coluna <c>ds_evento_externo_id</c>)</summary>
	public string? EventoExternoId { get; private set; }

	/// <summary>Tentativas de espelhamento que falharam (coluna <c>nr_tentativas_de_espelhamento</c>)</summary>
	public int TentativasDeEspelhamento { get; private set; }

	/// <summary>Momento a partir do qual vale tentar espelhar de novo (coluna <c>dt_proxima_tentativa_em</c>)</summary>
	public DateTimeOffset? ProximaTentativaEm { get; private set; }

	/// <summary>Motivo da última falha de espelhamento (coluna <c>ds_motivo_da_falha</c>)</summary>
	public string? MotivoDaFalha { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>)</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>
	/// Altera a reserva e sobe a versão. Com <paramref name="espelhar"/> o espelhamento volta a
	/// <see cref="EstadoDoEspelhamento.Aguardando"/> (mantendo o evento externo já criado); sem ele o estado não muda.
	/// </summary>
	/// <exception cref="DomainInvariantException">Mesmas regras do construtor.</exception>
	public void Alterar(
		Guid salaId,
		DateTimeOffset inicio,
		DateTimeOffset fim,
		string titulo,
		string? descricao,
		IEnumerable<ConvidadoDaReserva> convidados,
		bool espelhar)
	{
		AplicarDados(salaId, inicio, fim, titulo, descricao, convidados);

		Versao++;

		if (espelhar)
		{
			Espelhamento = EstadoDoEspelhamento.Aguardando;
			ReiniciarTentativas();
		}
	}

	/// <summary>Indica se o intervalo informado ocupa tempo em comum com a reserva (extremos que apenas se tocam não contam).</summary>
	public bool Sobrepoe(DateTimeOffset inicio, DateTimeOffset fim) => inicio < Fim && fim > Inicio;

	/// <summary>Indica se a reserva já começou em <paramref name="agora"/>.</summary>
	public bool JaComecou(DateTimeOffset agora) => Inicio <= agora;

	/// <summary>Registra o evento externo criado e dá o espelhamento por concluído.</summary>
	public void MarcarEspelhada(string eventoExternoId)
	{
		EventoExternoId = eventoExternoId;
		Espelhamento = EstadoDoEspelhamento.Espelhada;
		MotivoDaFalha = null;
		ReiniciarTentativas();
	}

	/// <summary>Registra uma falha que pode passar; agenda a próxima tentativa ou, na oitava, passa a <see cref="EstadoDoEspelhamento.Falhou"/>.</summary>
	public void RegistrarFalhaPassageira(string motivo, DateTimeOffset agora)
	{
		TentativasDeEspelhamento++;
		MotivoDaFalha = EsperaDeTentativas.Cortar(motivo);

		var espera = EsperaDeTentativas.Apos(TentativasDeEspelhamento);
		if (espera is null)
		{
			Espelhamento = EstadoDoEspelhamento.Falhou;
			ProximaTentativaEm = null;
			return;
		}

		ProximaTentativaEm = agora + espera.Value;
	}

	/// <summary>Registra uma falha sem conserto automático (ex.: falta de permissão).</summary>
	public void MarcarFalhaDefinitiva(string motivo)
	{
		MotivoDaFalha = EsperaDeTentativas.Cortar(motivo);
		ProximaTentativaEm = null;
		Espelhamento = EstadoDoEspelhamento.Falhou;
	}

	/// <summary>Reabre o espelhamento de uma reserva que falhou, zerando as tentativas.</summary>
	/// <exception cref="DomainInvariantException">O espelhamento não está em <see cref="EstadoDoEspelhamento.Falhou"/>.</exception>
	public void TentarDeNovo()
	{
		if (Espelhamento != EstadoDoEspelhamento.Falhou)
		{
			throw new DomainInvariantException("Só um espelhamento que falhou pode ser tentado de novo.");
		}

		Espelhamento = EstadoDoEspelhamento.Aguardando;
		ReiniciarTentativas();
	}

	private void ReiniciarTentativas()
	{
		TentativasDeEspelhamento = 0;
		ProximaTentativaEm = null;
	}

	private void AplicarDados(
		Guid salaId,
		DateTimeOffset inicio,
		DateTimeOffset fim,
		string titulo,
		string? descricao,
		IEnumerable<ConvidadoDaReserva> convidados)
	{
		if (string.IsNullOrWhiteSpace(titulo))
		{
			throw new DomainInvariantException("Uma reserva exige título não vazio.");
		}

		if (titulo.Length > TituloMaximo)
		{
			throw new DomainInvariantException($"O título da reserva aceita até {TituloMaximo} caracteres.");
		}

		if (descricao is { Length: > DescricaoMaxima })
		{
			throw new DomainInvariantException($"A descrição da reserva aceita até {DescricaoMaxima} caracteres.");
		}

		if (fim <= inicio)
		{
			throw new DomainInvariantException("O fim da reserva deve ser posterior ao início.");
		}

		GarantirNoPasso(inicio, "início");
		GarantirNoPasso(fim, "fim");

		var selecionados = SelecionarConvidados(convidados);

		SalaId = salaId;
		Inicio = inicio;
		Fim = fim;
		Titulo = titulo;
		Descricao = descricao;

		_convidados.Clear();
		foreach (var convidado in selecionados)
		{
			convidado.ReservaId = Id;
			_convidados.Add(convidado);
		}
	}

	private List<ConvidadoDaReserva> SelecionarConvidados(IEnumerable<ConvidadoDaReserva> convidados)
	{
		var semODono = convidados.Where(c => c.UsuarioId != ReservadoPorId).ToList();

		if (semODono.Select(c => c.UsuarioId).Distinct().Count() != semODono.Count)
		{
			throw new DomainInvariantException("Os convidados não podem se repetir.");
		}

		if (semODono.Count > MaximoDeConvidados)
		{
			throw new DomainInvariantException($"Uma reserva aceita até {MaximoDeConvidados} convidados.");
		}

		return semODono;
	}

	private static void GarantirNoPasso(DateTimeOffset momento, string nome)
	{
		var noMinuto = momento.Ticks % TimeSpan.TicksPerMinute == 0;

		if (!noMinuto || momento.Minute % PassoEmMinutos != 0)
		{
			throw new DomainInvariantException($"O {nome} da reserva deve cair em múltiplos de {PassoEmMinutos} minutos.");
		}
	}
}
