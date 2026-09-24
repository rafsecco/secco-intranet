using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Exclui um perfil comum sem membros.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class ExcluirPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="nome">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(string nome, CancellationToken cancellationToken = default)
	{
		var perfil = nome?.Trim() ?? string.Empty;

		if (perfil.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (ClassificacaoDePerfil.EhReservado(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		// Apagar o perfil de um setor quebraria o vínculo dele com o setor (ADR-0001); o do
		// produto é o que sustenta a autorização. Vale por convenção de nome: um perfil comum
		// que termine em -admin/-user também fica protegido, e isso é o lado seguro do erro.
		if (ClassificacaoDePerfil.Tipo(perfil) != TipoDePerfil.Comum)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilProtegido);
		}

		var excluido = await gestao.ExcluirPerfilAsync(perfil, cancellationToken).ConfigureAwait(false);

		if (excluido.IsFailure)
		{
			return excluido;
		}

		await AuditoriaDeAcesso.PerfilAsync(trilha, VerbosDeAuditoria.AcessoPerfilExcluir, perfil, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
