namespace Secco.Intranet.Web.Theming;

/// <summary>
/// Seleção do tema ativo (seção <c>Intranet:Theme</c>, ADR-0003/ADR-0004). Trocar de tema é
/// configuração, não deploy de código novo do core.
/// </summary>
public sealed class ThemeOptions
{
	/// <summary>Seção de configuração de onde estas opções são lidas.</summary>
	public const string SectionKey = "Intranet:Theme";

	/// <summary>Nome do tema de saída padrão (menu lateral).</summary>
	public const string DefaultTheme = "Vertical";

	/// <summary>
	/// Nome do tema ativo. Corresponde à pasta <c>/Themes/{Nome}/Views</c> dentro da RCL do
	/// tema — é assim que o <see cref="ThemeViewLocationExpander"/> o encontra.
	/// </summary>
	public string Nome { get; set; } = DefaultTheme;
}
