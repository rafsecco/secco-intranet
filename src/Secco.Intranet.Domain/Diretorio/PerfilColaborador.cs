using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Diretorio;

/// <summary>
/// Perfil complementar de um colaborador: só o que o SecureGate não guarda. Identidade (id,
/// e-mail, situação da conta) vive na plataforma; <see cref="UsuarioId"/> é um Guid dela, sem FK
/// (ADR-0006). Nasce na primeira edição — quem não tem perfil aparece pelo e-mail.
/// </summary>
public sealed class PerfilColaborador : BaseEntity
{
	/// <summary>Tamanho máximo do nome de exibição.</summary>
	public const int NomeMaxLength = 120;

	/// <summary>Tamanho máximo do cargo.</summary>
	public const int CargoMaxLength = 120;

	/// <summary>Tamanho máximo do ramal.</summary>
	public const int RamalMaxLength = 20;

	/// <summary>Tamanho máximo do texto "sobre".</summary>
	public const int SobreMaxLength = 500;

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoNome = "nome";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoRamal = "ramal";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoSobre = "sobre";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoCargo = "cargo";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoSetor = "setor";

	/// <summary>Nome de campo usado na auditoria.</summary>
	public const string CampoGestor = "gestor";

	private PerfilColaborador()
	{
		// Construtor de rehidratação do EF Core
	}

	/// <summary>Cria um perfil vazio para o usuário do SecureGate informado.</summary>
	/// <param name="usuarioId">Id do usuário no SecureGate.</param>
	/// <exception cref="DomainInvariantException">Se o id for vazio.</exception>
	public PerfilColaborador(Guid usuarioId)
	{
		if (usuarioId == Guid.Empty)
		{
			throw new DomainInvariantException("Um perfil de colaborador exige um usuário válido.");
		}

		UsuarioId = usuarioId;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Id do usuário no SecureGate. Único, sem FK.</summary>
	public Guid UsuarioId { get; private set; }

	/// <summary>Nome de exibição; nulo cai no e-mail na tela.</summary>
	public string? NomeExibicao { get; private set; }

	/// <summary>Cargo. Só o admin do diretório edita.</summary>
	public string? Cargo { get; private set; }

	/// <summary>Ramal telefônico.</summary>
	public string? Ramal { get; private set; }

	/// <summary>Texto livre sobre a pessoa.</summary>
	public string? Sobre { get; private set; }

	/// <summary>Setor de lotação (FK para o setor local). Só o admin do diretório edita.</summary>
	public Guid? SetorId { get; private set; }

	/// <summary>Id, no SecureGate, do gestor a quem a pessoa reporta. Sem FK. Só o admin edita.</summary>
	public Guid? GestorUsuarioId { get; private set; }

	/// <summary>Momento da criação.</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Momento da última alteração real (nulo se nunca alterado).</summary>
	public DateTimeOffset? UpdatedAt { get; private set; }

	/// <summary>Altera o contato. Devolve os nomes dos campos que <b>de fato</b> mudaram.</summary>
	/// <exception cref="DomainInvariantException">Algum campo acima do limite; nada é alterado.</exception>
	public IReadOnlyList<string> EditarContato(string? nome, string? ramal, string? sobre)
	{
		var novoNome = Normalizar(nome, NomeMaxLength, "nome");
		var novoRamal = Normalizar(ramal, RamalMaxLength, "ramal");
		var novoSobre = Normalizar(sobre, SobreMaxLength, "sobre");

		var alterados = new List<string>();

		Atribuir(NomeExibicao, novoNome, valor => NomeExibicao = valor, CampoNome, alterados);
		Atribuir(Ramal, novoRamal, valor => Ramal = valor, CampoRamal, alterados);
		Atribuir(Sobre, novoSobre, valor => Sobre = valor, CampoSobre, alterados);

		if (alterados.Count > 0)
		{
			UpdatedAt = DateTimeOffset.UtcNow;
		}

		return alterados;
	}

	/// <summary>
	/// Altera os dados funcionais (valores nulos limpam o campo). Devolve os nomes dos campos que
	/// de fato mudaram.
	/// </summary>
	/// <exception cref="DomainInvariantException">Cargo acima do limite, ou gestor igual ao próprio usuário.</exception>
	public IReadOnlyList<string> EditarDadosFuncionais(string? cargo, Guid? setorId, Guid? gestorUsuarioId)
	{
		var novoCargo = Normalizar(cargo, CargoMaxLength, "cargo");
		var novoSetor = setorId == Guid.Empty ? null : setorId;
		var novoGestor = gestorUsuarioId == Guid.Empty ? null : gestorUsuarioId;

		if (novoGestor == UsuarioId)
		{
			throw new DomainInvariantException("Ninguém pode ser gestor de si mesmo.");
		}

		var alterados = new List<string>();

		Atribuir(Cargo, novoCargo, valor => Cargo = valor, CampoCargo, alterados);

		if (SetorId != novoSetor)
		{
			SetorId = novoSetor;
			alterados.Add(CampoSetor);
		}

		if (GestorUsuarioId != novoGestor)
		{
			GestorUsuarioId = novoGestor;
			alterados.Add(CampoGestor);
		}

		if (alterados.Count > 0)
		{
			UpdatedAt = DateTimeOffset.UtcNow;
		}

		return alterados;
	}

	private static void Atribuir(string? atual, string? novo, Action<string?> definir, string campo, List<string> alterados)
	{
		if (string.Equals(atual, novo, StringComparison.Ordinal))
		{
			return;
		}

		definir(novo);
		alterados.Add(campo);
	}

	private static string? Normalizar(string? valor, int limite, string rotulo)
	{
		var aparado = valor?.Trim();

		if (string.IsNullOrEmpty(aparado))
		{
			return null;
		}

		if (aparado.Length > limite)
		{
			throw new DomainInvariantException($"O campo {rotulo} excede o limite de {limite} caracteres.");
		}

		return aparado;
	}
}
