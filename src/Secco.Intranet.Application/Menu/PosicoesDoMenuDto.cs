namespace Secco.Intranet.Application.Menu;

/// <summary>Posições salvas do menu, por grupo; chaves comparadas sem diferenciar caixa.</summary>
/// <param name="Principal">Posições do grupo principal.</param>
/// <param name="Setores">Posições do grupo de setores.</param>
public sealed record PosicoesDoMenuDto(IReadOnlyDictionary<string, int> Principal, IReadOnlyDictionary<string, int> Setores)
{
	/// <summary>Sem nenhuma posição salva: tudo na ordem padrão.</summary>
	public static PosicoesDoMenuDto Vazias { get; } = new(
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
}
