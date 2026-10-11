using System.Net.Mail;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Diretorio;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>Pedido de cadastro de uma pessoa no tenant administrado.</summary>
/// <param name="Email">E-mail.</param>
/// <param name="Nome">Nome de exibição (opcional).</param>
/// <param name="Perfis">Perfis iniciais.</param>
/// <param name="EntraPorSenha"><c>true</c>: convite para senha; <c>false</c>: só pelo Entra.</param>
public sealed record CadastrarUsuarioDoTenantCommand(string? Email, string? Nome, IReadOnlyList<string>? Perfis, bool EntraPorSenha);

/// <summary>
/// Pessoas de um tenant administrado. O cadastro é a única porta de entrada: o login federado da
/// plataforma é fail-closed e só casa quem já existe (ADR-0026). Sem as regras de "último
/// intranet-admin" e "a própria conta" — o intranet-admin não é usuário daquele tenant.
/// </summary>
/// <param name="gestao">Porta.</param>
/// <param name="trilha">Trilha da Intranet.</param>
public sealed class UsuariosDoTenantHandler(IGestaoDeAcessoDoTenant gestao, ITrilhaDeAuditoria trilha)
{
	/// <summary>Cadastra a pessoa; devolve o id criado.</summary>
	public async Task<Result<Guid>> CadastrarAsync(Guid tenantId, CadastrarUsuarioDoTenantCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var email = command.Email?.Trim() ?? string.Empty;

		if (!EmailValido(email))
		{
			return Result.Failure<Guid>(IntranetErrors.Acesso.EmailInvalido);
		}

		var nome = NomeDeExibicao.Normalizar(command.Nome);

		if (nome is { Length: > NomeDeExibicao.MaxLength })
		{
			return Result.Failure<Guid>(IntranetErrors.Acesso.CadastroRecusado);
		}

		IReadOnlyList<string> perfis = [.. (command.Perfis ?? []).Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal)];

		var perfisValidos = await ConferirPerfisAsync(tenantId, perfis, cancellationToken).ConfigureAwait(false);

		if (perfisValidos.IsFailure)
		{
			return Result.Failure<Guid>(perfisValidos.Error);
		}

		if (!command.EntraPorSenha)
		{
			var federacao = await gestao.ObterFederacaoAsync(tenantId, cancellationToken).ConfigureAwait(false);

			if (federacao.IsFailure)
			{
				return Result.Failure<Guid>(federacao.Error);
			}

			if (!federacao.Value.Habilitada)
			{
				return Result.Failure<Guid>(IntranetErrors.Acesso.EntraSemFederacao);
			}
		}

		var criado = await gestao
			.CadastrarUsuarioAsync(tenantId, new NovoUsuarioDoTenant(email, nome, perfis, command.EntraPorSenha), cancellationToken)
			.ConfigureAwait(false);

		if (criado.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.CadastroAsync(trilha, tenantId, criado.Value, command.EntraPorSenha, perfis, cancellationToken)
				.ConfigureAwait(false);
		}

		return criado;
	}

	/// <summary>Atribui um perfil não reservado.</summary>
	public async Task<Result> AtribuirPerfilAsync(Guid tenantId, Guid usuarioId, string? perfil, CancellationToken cancellationToken = default)
	{
		var nome = perfil?.Trim() ?? string.Empty;
		var conferido = await ConferirPerfisAsync(tenantId, [nome], cancellationToken).ConfigureAwait(false);

		if (conferido.IsFailure)
		{
			return conferido;
		}

		var atribuido = await gestao.AtribuirPerfilAsync(tenantId, usuarioId, nome, cancellationToken).ConfigureAwait(false);

		return await RegistrarSeOkAsync(atribuido, VerbosDeAuditoria.TenantAcessoPerfilAtribuir, tenantId, usuarioId, nome, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>Retira um perfil não reservado.</summary>
	public async Task<Result> RetirarPerfilAsync(Guid tenantId, Guid usuarioId, string? perfil, CancellationToken cancellationToken = default)
	{
		var nome = perfil?.Trim() ?? string.Empty;

		if (nome.Length == 0)
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (ClassificacaoDePerfil.EhReservado(nome))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		var retirado = await gestao.RetirarPerfilAsync(tenantId, usuarioId, nome, cancellationToken).ConfigureAwait(false);

		return await RegistrarSeOkAsync(retirado, VerbosDeAuditoria.TenantAcessoPerfilRetirar, tenantId, usuarioId, nome, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>Reenvia o convite — só a quem entra por senha e ainda não a definiu.</summary>
	public async Task<Result> ReenviarConviteAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure(usuario.Error);
		}

		if (usuario.Value.TemSenha || !usuario.Value.EntraPorSenha)
		{
			return Result.Failure(IntranetErrors.Acesso.ConviteNaoCabe);
		}

		var reenviado = await gestao.ReenviarConviteAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false);

		return await RegistrarSeOkAsync(reenviado, VerbosDeAuditoria.TenantAcessoConviteReenviar, tenantId, usuarioId, null, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>Desativa a conta.</summary>
	public async Task<Result> DesativarAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		await RegistrarSeOkAsync(
			await gestao.DesativarUsuarioAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false),
			VerbosDeAuditoria.TenantAcessoUsuarioDesativar, tenantId, usuarioId, null, cancellationToken).ConfigureAwait(false);

	/// <summary>Reativa a conta.</summary>
	public async Task<Result> ReativarAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		await RegistrarSeOkAsync(
			await gestao.ReativarUsuarioAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false),
			VerbosDeAuditoria.TenantAcessoUsuarioReativar, tenantId, usuarioId, null, cancellationToken).ConfigureAwait(false);

	/// <summary>Encerra as sessões.</summary>
	public async Task<Result> EncerrarSessoesAsync(Guid tenantId, Guid usuarioId, CancellationToken cancellationToken = default) =>
		await RegistrarSeOkAsync(
			await gestao.EncerrarSessoesAsync(tenantId, usuarioId, cancellationToken).ConfigureAwait(false),
			VerbosDeAuditoria.TenantAcessoSessoesEncerrar, tenantId, usuarioId, null, cancellationToken).ConfigureAwait(false);

	private static bool EmailValido(string email) =>
		email.Length is > 0 and <= 256
		&& !email.Any(char.IsWhiteSpace)
		&& MailAddress.TryCreate(email, out var endereco)
		&& string.Equals(endereco.Address, email, StringComparison.Ordinal);

	private async Task<Result> ConferirPerfisAsync(Guid tenantId, IReadOnlyList<string> perfis, CancellationToken cancellationToken)
	{
		if (perfis.Any(p => p.Length == 0))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilRequired);
		}

		if (perfis.Any(ClassificacaoDePerfil.EhReservado))
		{
			return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
		}

		if (perfis.Count == 0)
		{
			return Result.Success();
		}

		var existentes = await gestao.ListarPerfisAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (existentes.IsFailure)
		{
			return Result.Failure(existentes.Error);
		}

		var porNome = existentes.Value.ToDictionary(p => p.Nome, StringComparer.Ordinal);

		foreach (var perfil in perfis)
		{
			if (!porNome.TryGetValue(perfil, out var existente))
			{
				return Result.Failure(IntranetErrors.Acesso.PerfilNaoEncontrado);
			}

			if (existente.Reservado)
			{
				return Result.Failure(IntranetErrors.Acesso.PerfilReservado);
			}
		}

		return Result.Success();
	}

	private async Task<Result> RegistrarSeOkAsync(
		Result resultado, string verbo, Guid tenantId, Guid usuarioId, string? perfil, CancellationToken cancellationToken)
	{
		if (resultado.IsSuccess)
		{
			await AuditoriaDoAcessoDoTenant.UsuarioAsync(trilha, verbo, tenantId, usuarioId, perfil, cancellationToken).ConfigureAwait(false);
		}

		return resultado;
	}
}
