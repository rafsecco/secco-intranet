using System.Text.Json;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Publicacoes.Notificacao;
using Secco.Intranet.Domain.Vencimentos;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Trechos comuns aos casos de uso de vencimento.</summary>
internal static class ApoioDeVencimentos
{
	/// <summary>Nome gravado como autor quando não há usuário identificado (modo aberto de DEV).</summary>
	internal const string AutorDoSistema = "sistema";

	/// <summary>
	/// O responsável precisa ter a Role <c>{slug}-admin</c> ou <c>{slug}-user</c>. Diretório vazio
	/// (SecureGate indisponível) recusa: sem como provar o acesso, falha fechado.
	/// </summary>
	internal static async Task<bool> ResponsavelTemAcessoAsync(
		IDiretorioDeUsuarios diretorio, Guid responsavelId, string setorSlug, CancellationToken cancellationToken)
	{
		var usuarios = await diretorio.ListarDoTenantAtualAsync(cancellationToken).ConfigureAwait(false);
		var roles = new[] { $"{setorSlug}-admin", $"{setorSlug}-user" };

		return usuarios.Any(usuario => usuario.Id == responsavelId
			&& usuario.Roles.Any(role => roles.Contains(role, StringComparer.OrdinalIgnoreCase)));
	}

	/// <summary>Metadados da trilha: título, setor e datas em ISO, sem conteúdo.</summary>
	internal static string Metadata(
		ItemVencimento item, string setorSlug, DateOnly? dataAnterior = null, DateOnly? dataNova = null)
	{
		var campos = new Dictionary<string, string?>
		{
			["titulo"] = item.Titulo,
			["setor"] = setorSlug,
			["dataVencimento"] = Iso(item.DataVencimento),
		};

		if (dataAnterior is not null)
		{
			campos["dataAnterior"] = Iso(dataAnterior.Value);
		}

		if (dataNova is not null)
		{
			campos["dataNova"] = Iso(dataNova.Value);
		}

		return JsonSerializer.Serialize(campos);
	}

	private static string Iso(DateOnly data) => data.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
