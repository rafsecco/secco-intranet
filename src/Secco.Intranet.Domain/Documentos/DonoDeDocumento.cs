using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Documentos;

/// <summary>
/// Dono de um documento: exatamente um entre um setor e uma biblioteca.
/// </summary>
/// <param name="SetorId">Setor dono; nulo quando o dono é uma biblioteca.</param>
/// <param name="BibliotecaId">Biblioteca dona; nula quando o dono é um setor.</param>
public readonly record struct DonoDeDocumento(Guid? SetorId, Guid? BibliotecaId)
{
	/// <summary>Dono setor.</summary>
	/// <param name="setorId">Identificador do setor. Obrigatório.</param>
	/// <exception cref="DomainInvariantException">Se o identificador for vazio.</exception>
	public static DonoDeDocumento DoSetor(Guid setorId) =>
		setorId == Guid.Empty
			? throw new DomainInvariantException("Um documento exige um setor dono.")
			: new(setorId, null);

	/// <summary>Dono biblioteca.</summary>
	/// <param name="bibliotecaId">Identificador da biblioteca. Obrigatório.</param>
	/// <exception cref="DomainInvariantException">Se o identificador for vazio.</exception>
	public static DonoDeDocumento DaBiblioteca(Guid bibliotecaId) =>
		bibliotecaId == Guid.Empty
			? throw new DomainInvariantException("Um documento exige uma biblioteca dona.")
			: new(null, bibliotecaId);

	/// <summary>Verdadeiro quando exatamente um dos dois identificadores está preenchido e não é vazio.</summary>
	public bool EhValido =>
		(SetorId is null) != (BibliotecaId is null)
		&& (SetorId ?? BibliotecaId) != Guid.Empty;
}
