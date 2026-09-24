using System.ComponentModel.DataAnnotations;
using Secco.Intranet.Domain.Diretorio;

namespace Secco.Intranet.Web.Models.Diretorio;

/// <summary>
/// Formulário do <b>próprio colaborador</b>: só contato. Não tem cargo, setor nem gestor de
/// propósito — um campo forjado no corpo da requisição não tem onde ser ligado (o binder ignora
/// o que o modelo não declara), e o handler correspondente também não os conhece.
/// </summary>
public class EditarContatoForm
{
	/// <summary>Nome de exibição.</summary>
	[StringLength(PerfilColaborador.NomeMaxLength)]
	public string? Nome { get; set; }

	/// <summary>Ramal.</summary>
	[StringLength(PerfilColaborador.RamalMaxLength)]
	public string? Ramal { get; set; }

	/// <summary>Texto livre sobre a pessoa.</summary>
	[StringLength(PerfilColaborador.SobreMaxLength)]
	public string? Sobre { get; set; }
}

/// <summary>Formulário do <b>admin do diretório</b>: contato mais os dados funcionais.</summary>
public sealed class EditarPessoaForm : EditarContatoForm
{
	/// <summary>Cargo.</summary>
	[StringLength(PerfilColaborador.CargoMaxLength)]
	public string? Cargo { get; set; }

	/// <summary>Setor de lotação.</summary>
	public Guid? SetorId { get; set; }

	/// <summary>Id do gestor no SecureGate.</summary>
	public Guid? GestorUsuarioId { get; set; }
}
