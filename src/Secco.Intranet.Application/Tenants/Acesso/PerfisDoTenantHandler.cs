using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// Perfis de um tenant administrado: criar, excluir e definir permissões. Sem a convenção de
/// setor nem os perfis do produto — são da Intranet, não do sistema administrado.
/// </summary>
/// <param name="gestao">Porta.</param>
/// <param name="trilha">Trilha da Intranet.</param>
public sealed class PerfisDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Cria um perfil.</summary>
	public async Task<Result> CriarAsync(Guid tenantId, string? nome, CancellationToken cancellationToken = default)
	{
		var valido = Validar(nome);

		if (valido.IsFailure)
		{
			return valido;
		}

		var aparado = nome!.Trim();
		var criado = await gestao.CriarPerfilAsync(tenantId, aparado, cancellationToken).ConfigureAwait(false);

		if (criado.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.PerfilAsync(trilha, VerbosDeAuditoria.TenantAcessoPerfilCriar, tenantId, aparado, cancellationToken)
				.ConfigureAwait(false);
		}

		return criado;
	}

	/// <summary>Exclui um perfil sem membros.</summary>
	public async Task<Result> ExcluirAsync(Guid tenantId, string? nome, CancellationToken cancellationToken = default)
	{
		var valido = Validar(nome);

		if (valido.IsFailure)
		{
			return valido;
		}

		var perfil = await gestao.ObterPerfilAsync(tenantId, nome!.Trim(), cancellationToken).ConfigureAwait(false);

		if (perfil.IsFailure)
		{
			return Result.Failure(perfil.Error);
		}

		if (perfil.Value.Reservado)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		if (perfil.Value.TotalDeMembros > 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilComMembros);
		}

		var excluido = await gestao.ExcluirPerfilAsync(tenantId, perfil.Value.Nome, cancellationToken).ConfigureAwait(false);

		if (excluido.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.PerfilAsync(trilha, VerbosDeAuditoria.TenantAcessoPerfilExcluir, tenantId, perfil.Value.Nome, cancellationToken)
				.ConfigureAwait(false);
		}

		return excluido;
	}

	/// <summary>Substitui a lista de permissões do perfil pelas marcadas.</summary>
	public async Task<Result> DefinirPermissoesAsync(
		Guid tenantId, string? nome, IReadOnlyCollection<string>? permissoes, CancellationToken cancellationToken = default)
	{
		var lista = ListaDePermissoes.Ler(permissoes);

		if (lista.IsFailure)
		{
			return Result.Failure(lista.Error);
		}

		var perfil = await gestao.ObterPerfilAsync(tenantId, nome?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);

		if (perfil.IsFailure)
		{
			return Result.Failure(perfil.Error);
		}

		if (perfil.Value.Reservado)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var definido = await gestao.DefinirPermissoesDoPerfilAsync(tenantId, perfil.Value.Nome, lista.Value, cancellationToken).ConfigureAwait(false);

		if (definido.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant
				.PermissoesAsync(trilha, tenantId, perfil.Value.Nome, perfil.Value.Permissoes.Count, lista.Value.Count, cancellationToken)
				.ConfigureAwait(false);
		}

		return definido;
	}

	private static Result Validar(string? nome)
	{
		var aparado = nome?.Trim() ?? string.Empty;

		if (aparado.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (ClassificacaoDePerfil.EhReservado(aparado))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		return ClassificacaoDePerfil.NomeValido(aparado) ? Result.Success() : Result.Failure(IntranetErrors.Acesso.PerfilNomeInvalido);
	}
}
