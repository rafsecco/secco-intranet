namespace Secco.Intranet.Web.Models.Tenants;

/// <summary>Tela que explica por que a área de tenants exige segundo fator.</summary>
/// <param name="UrlDoCadastro">Página de cadastro do 2FA no SecureGate, quando a URL é conhecida.</param>
public sealed record SegundoFatorViewModel(string? UrlDoCadastro);
