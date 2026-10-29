namespace Secco.Intranet.Web.Extensibility;

/// <summary>
/// Registro de um tipo de <see cref="IItemDeMenuPrincipal"/> pelo DI, para quem não quer depender
/// da descoberta pelas application parts. O catálogo do core junta os dois caminhos sem repetir tipo.
/// </summary>
/// <param name="Tipo">Tipo da implementação.</param>
public sealed record RegistroDeItemDeMenuPrincipal(Type Tipo);
