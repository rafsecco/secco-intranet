using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>
/// Transforma um vencimento a avisar nos lotes que o Hub recebe. Parte pura do aviso: nada
/// aqui toca banco, HTTP ou relógio.
/// </summary>
internal static class AvisoDeVencimento
{
	/// <summary>Valor de <c>Source</c> em toda notificação criada pelos Vencimentos.</summary>
	internal const string Origem = "vencimentos";

	/// <summary>Valor de <c>Type</c> em toda notificação criada pelos Vencimentos.</summary>
	internal const string Tipo = "aviso";

	/// <summary>Faltando mais que isso o aviso vai só ao sino; até isso, vai também por e-mail.</summary>
	private const int DiasSoSino = 7;

	/// <summary>Canais do aviso: sino para marcos distantes, sino e e-mail de 7 dias em diante. Nunca Teams nem Slack.</summary>
	/// <param name="diasRestantes">Dias até o vencimento.</param>
	internal static IReadOnlyList<string> Canais(int diasRestantes) =>
		diasRestantes > DiasSoSino
			? [CanaisDaPrioridade.InApp]
			: [CanaisDaPrioridade.InApp, CanaisDaPrioridade.Email];

	/// <summary>
	/// Quem recebe: os administradores do setor e o responsável do item — este só se ainda tiver
	/// algum role do setor, porque quem saiu do setor não deve continuar sendo avisado.
	/// </summary>
	/// <param name="usuarios">Usuários do tenant.</param>
	/// <param name="setorSlug">Slug do setor dono.</param>
	/// <param name="responsavelId">Responsável do item, se houver.</param>
	internal static IReadOnlyList<UsuarioDoTenant> Destinatarios(
		IReadOnlyList<UsuarioDoTenant> usuarios,
		string setorSlug,
		Guid? responsavelId)
	{
		var admin = $"{setorSlug}-admin";
		var user = $"{setorSlug}-user";

		return usuarios
			.Where(usuario =>
				usuario.Roles.Contains(admin, StringComparer.OrdinalIgnoreCase)
				|| (usuario.Id == responsavelId
					&& usuario.Roles.Contains(user, StringComparer.OrdinalIgnoreCase)))
			.DistinctBy(usuario => usuario.Id)
			.ToList();
	}

	/// <summary>
	/// Monta os lotes. Com e-mail nos canais, quem tem e-mail vai num lote <c>in_app</c>+<c>email</c>
	/// e quem não tem vai num lote só <c>in_app</c> — o lote do Hub é tudo-ou-nada, e um destino
	/// sem e-mail reprovaria o lote inteiro. Lotes vazios não saem; cada um é fatiado em 500.
	/// </summary>
	/// <param name="destinatarios">Quem recebe.</param>
	/// <param name="canais">Canais pedidos.</param>
	internal static IReadOnlyList<(IReadOnlyList<string> Canais, IReadOnlyList<DestinoDaMensagem> Destinos)> Lotes(
		IReadOnlyList<UsuarioDoTenant> destinatarios,
		IReadOnlyList<string> canais)
	{
		var lotes = new List<(IReadOnlyList<string> Canais, IReadOnlyList<DestinoDaMensagem> Destinos)>();

		if (!canais.Contains(CanaisDaPrioridade.Email, StringComparer.Ordinal))
		{
			Acrescentar(lotes, canais, destinatarios.Select(u => new DestinoDaMensagem(u.Id, null)));

			return lotes;
		}

		Acrescentar(
			lotes,
			canais,
			destinatarios.Where(u => !string.IsNullOrWhiteSpace(u.Email)).Select(u => new DestinoDaMensagem(u.Id, u.Email)));

		Acrescentar(
			lotes,
			[CanaisDaPrioridade.InApp],
			destinatarios.Where(u => string.IsNullOrWhiteSpace(u.Email)).Select(u => new DestinoDaMensagem(u.Id, null)));

		return lotes;
	}

	/// <summary>Título da notificação, com os dias reais até o vencimento.</summary>
	/// <param name="titulo">Título do item.</param>
	/// <param name="diasRestantes">Dias até o vencimento.</param>
	internal static string Titulo(string titulo, int diasRestantes) =>
		diasRestantes switch
		{
			0 => $"{titulo} vence hoje",
			1 => $"{titulo} vence em 1 dia",
			_ => $"{titulo} vence em {diasRestantes} dias",
		};

	private static void Acrescentar(
		List<(IReadOnlyList<string> Canais, IReadOnlyList<DestinoDaMensagem> Destinos)> lotes,
		IReadOnlyList<string> canais,
		IEnumerable<DestinoDaMensagem> destinos)
	{
		foreach (var bloco in destinos.Chunk(AvisoDePublicacao.MaxDestinosPorLote))
		{
			lotes.Add((canais, bloco));
		}
	}
}
