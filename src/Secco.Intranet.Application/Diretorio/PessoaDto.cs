namespace Secco.Intranet.Application.Diretorio;

/// <summary>Uma pessoa do diretório: identidade do SecureGate mais o perfil complementar local.</summary>
/// <param name="UsuarioId">Id no SecureGate.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição; sem perfil, o e-mail (e, sem e-mail, o id).</param>
/// <param name="Cargo">Cargo.</param>
/// <param name="Ramal">Ramal.</param>
/// <param name="Sobre">Texto livre.</param>
/// <param name="SetorId">Setor de lotação.</param>
/// <param name="SetorNome">Nome do setor.</param>
/// <param name="SetorSlug">Slug do setor (define a cor).</param>
/// <param name="SetorAtivo">Se o setor de lotação está ativo (esmaece o badge quando não).</param>
/// <param name="GestorUsuarioId">Id do gestor, quando definido.</param>
/// <param name="GestorNome">Nome do gestor, quando ele é um usuário ativo.</param>
/// <param name="GestorInativo">Verdadeiro se há gestor definido mas ele não é mais um usuário ativo.</param>
/// <param name="TemPerfil">Se existe perfil local para a pessoa.</param>
public sealed record PessoaDto(
	Guid UsuarioId,
	string Email,
	string Nome,
	string? Cargo,
	string? Ramal,
	string? Sobre,
	Guid? SetorId,
	string? SetorNome,
	string? SetorSlug,
	bool SetorAtivo,
	Guid? GestorUsuarioId,
	string? GestorNome,
	bool GestorInativo,
	bool TemPerfil);
