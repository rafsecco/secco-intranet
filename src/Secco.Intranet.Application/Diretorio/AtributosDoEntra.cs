namespace Secco.Intranet.Application.Diretorio;

/// <summary>
/// Atributos da pessoa que o SecureGate leu do diretório federado (Entra ID). Só existe quando a
/// plataforma marcou a origem como <c>directory</c>; os textos já vêm aparados e branco vira nulo.
/// </summary>
/// <param name="Cargo">Cargo (<c>jobTitle</c>).</param>
/// <param name="Departamento">Departamento.</param>
/// <param name="Telefone">Telefone comercial.</param>
/// <param name="GestorUsuarioId">Id, no SecureGate, do gestor da pessoa.</param>
public sealed record AtributosDoEntra(string? Cargo, string? Departamento, string? Telefone, Guid? GestorUsuarioId);
