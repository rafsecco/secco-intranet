using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application;

/// <summary>Erros de negócio do produto (ADR-0004): códigos estáveis <c>Intranet.*</c>.</summary>
public static class IntranetErrors
{
	/// <summary>Erros do recurso Setor.</summary>
	public static class Setores
	{
		/// <summary>Nome ausente ou vazio.</summary>
		public static readonly Error NomeRequired =
			Error.Validation("Intranet.Setor.NomeRequired", "O nome é obrigatório.");

		/// <summary>Nome acima do limite configurado.</summary>
		public static Error NomeTooLong(int limit) =>
			Error.Validation("Intranet.Setor.NomeTooLong", $"O nome excede o limite de {limit} caracteres.");

		/// <summary>Slug ausente ou vazio.</summary>
		public static readonly Error SlugRequired =
			Error.Validation("Intranet.Setor.SlugRequired", "O slug é obrigatório.");

		/// <summary>Já existe um setor com esse slug no tenant atual.</summary>
		public static Error SlugAlreadyExists(string slug) =>
			Error.Conflict("Intranet.Setor.SlugAlreadyExists", $"Já existe um setor com o slug '{slug}'.");

		/// <summary>Registro não encontrado no banco do tenant atual.</summary>
		public static readonly Error NotFound =
			Error.NotFound("Intranet.Setor.NotFound", "Setor não encontrado.");

		/// <summary>
		/// Falha ao provisionar as Roles <c>{slug}-admin</c>/<c>{slug}-user</c> do setor no
		/// SecureGate (ADR-0001). Mensagem sem detalhe interno (ADR-0020).
		/// </summary>
		public static readonly Error AccessProvisioningUnavailable =
			Error.Failure(
				"Intranet.Setor.AccessProvisioningUnavailable",
				"Não foi possível provisionar o acesso do setor no momento. Tente novamente em instantes.");
	}

	/// <summary>Erros do recurso Documento.</summary>
	public static class Documentos
	{
		/// <summary>Título ausente ou vazio.</summary>
		public static readonly Error TituloRequired =
			Error.Validation("Intranet.Documento.TituloRequired", "O título é obrigatório.");

		/// <summary>Título acima do limite configurado.</summary>
		public static Error TituloTooLong(int limit) =>
			Error.Validation("Intranet.Documento.TituloTooLong", $"O título excede o limite de {limit} caracteres.");

		/// <summary>Nenhum arquivo enviado, ou arquivo sem conteúdo.</summary>
		public static readonly Error ArquivoRequired =
			Error.Validation("Intranet.Documento.ArquivoRequired", "Escolha um arquivo para publicar.");

		/// <summary>Arquivo acima do tamanho máximo aceito.</summary>
		public static Error ArquivoMuitoGrande(long limiteBytes) =>
			Error.Validation(
				"Intranet.Documento.ArquivoMuitoGrande",
				$"O arquivo excede o tamanho máximo de {limiteBytes / (1024 * 1024)} MB.");

		/// <summary>
		/// Extensão fora da lista aceita, ou conteúdo que não corresponde à extensão — os dois
		/// casos compartilham a mensagem de propósito: distinguir ajudaria a sondar o validador.
		/// </summary>
		public static readonly Error ArquivoNaoAceito =
			Error.Validation(
				"Intranet.Documento.ArquivoNaoAceito",
				"Tipo de arquivo não aceito, ou o conteúdo não corresponde à extensão.");

		/// <summary>Registro não encontrado no banco do tenant atual.</summary>
		public static readonly Error NotFound =
			Error.NotFound("Intranet.Documento.NotFound", "Documento não encontrado.");

		/// <summary>Falha ao gravar ou ler o conteúdo. Mensagem sem detalhe interno (ADR-0020).</summary>
		public static readonly Error ArmazenamentoIndisponivel =
			Error.Failure(
				"Intranet.Documento.ArmazenamentoIndisponivel",
				"Não foi possível acessar o arquivo no momento. Tente novamente em instantes.");
	}
}
