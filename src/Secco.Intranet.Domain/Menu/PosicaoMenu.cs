using Secco.SharedKernel.Entities;

namespace Secco.Intranet.Domain.Menu;

/// <summary>Grupo do menu principal cuja ordem o administrador pode salvar.</summary>
public enum GrupoDoMenu
{
	/// <summary>Itens do topo do menu: Mural, Diretório e itens extras.</summary>
	Principal = 1,

	/// <summary>Setores listados no menu.</summary>
	Setores = 2,
}

/// <summary>
/// Posição salva de um item do menu dentro de um grupo, por tenant. Itens sem linha seguem a ordem padrão.
/// </summary>
public sealed class PosicaoMenu : BaseEntity
{
	private PosicaoMenu()
	{
		// Construtor de rehidratação do EF Core
		Chave = string.Empty;
	}

	/// <summary>Cria a posição de um item.</summary>
	/// <param name="grupo">Grupo do menu.</param>
	/// <param name="chave">Chave do item: <c>mural</c>, <c>diretorio</c>, chave do extra ou Id do setor em formato <c>D</c>.</param>
	/// <param name="ordem">Posição no grupo, a partir de zero.</param>
	public PosicaoMenu(GrupoDoMenu grupo, string chave, int ordem)
	{
		Grupo = grupo;
		Chave = chave;
		Ordem = ordem;
	}

	/// <summary>Grupo do menu.</summary>
	public GrupoDoMenu Grupo { get; private set; }

	/// <summary>Chave do item dentro do grupo.</summary>
	public string Chave { get; private set; }

	/// <summary>Posição no grupo, a partir de zero.</summary>
	public int Ordem { get; private set; }
}
