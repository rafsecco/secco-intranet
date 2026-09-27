using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Cria um perfil no SecureGate.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class CriarPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>
	/// Permissões mínimas de cada perfil do produto que o modelo de permissões já conhece
	/// (ADR-0021) — garantidas assim que o perfil nasce, para não depender de reconciliação
	/// manual logo na criação. Perfis comuns e de setor não entram aqui.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> PermissoesBaseDoProduto =
		new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
		{
			[ClassificacaoDePerfil.DiretorioAdmin] = [IntranetPermissoes.Diretorio.Read, IntranetPermissoes.Diretorio.Manage],
			[ClassificacaoDePerfil.DiretorioUsuario] = [IntranetPermissoes.Diretorio.Read],
		};

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="nome">Nome do perfil (a tela mostra o formato aceito).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(string nome, CancellationToken cancellationToken = default)
	{
		var perfil = nome?.Trim() ?? string.Empty;

		if (!ClassificacaoDePerfil.NomeValido(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilNomeInvalido);
		}

		if (ClassificacaoDePerfil.EhReservado(perfil))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var criado = await gestao.CriarPerfilAsync(perfil, cancellationToken).ConfigureAwait(false);

		if (criado.IsFailure)
		{
			return criado;
		}

		if (PermissoesBaseDoProduto.TryGetValue(perfil, out var permissoesBase))
		{
			// Best-effort: o perfil já foi criado com sucesso; se a permissão falhar, a
			// reconciliação cobre o que faltou.
			await gestao.GarantirPermissoesAsync(perfil, permissoesBase, cancellationToken).ConfigureAwait(false);
		}

		await AuditoriaDeAcesso.PerfilAsync(trilha, VerbosDeAuditoria.AcessoPerfilCriar, perfil, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
