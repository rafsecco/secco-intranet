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

		/// <summary>Tentativa de desativar um setor fixo do sistema.</summary>
		public static readonly Error FixoNaoDesativa =
			Error.Validation(
				"Intranet.Setor.FixoNaoDesativa",
				"Um setor fixo do sistema não pode ser desativado.");

		/// <summary>Ícone fora do formato do Bootstrap Icons.</summary>
		public static readonly Error IconeInvalido =
			Error.Validation(
				"Intranet.Setor.IconeInvalido",
				"O ícone precisa ser uma classe do Bootstrap Icons, como 'bi-cash-coin'.");

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

	/// <summary>Erros do recurso Publicação.</summary>
	public static class Publicacoes
	{
		/// <summary>Título ausente ou vazio.</summary>
		public static readonly Error TituloRequired =
			Error.Validation("Intranet.Publicacao.TituloRequired", "O título é obrigatório.");

		/// <summary>Título acima do limite configurado.</summary>
		/// <param name="limit">Limite aplicado.</param>
		public static Error TituloTooLong(int limit) =>
			Error.Validation("Intranet.Publicacao.TituloTooLong", $"O título excede o limite de {limit} caracteres.");

		/// <summary>Corpo ausente ou vazio.</summary>
		public static readonly Error CorpoRequired =
			Error.Validation("Intranet.Publicacao.CorpoRequired", "O corpo é obrigatório.");

		/// <summary>Corpo acima do limite da coluna.</summary>
		public static readonly Error CorpoTooLong =
			Error.Validation("Intranet.Publicacao.CorpoTooLong", "O corpo excede o limite de 4000 caracteres.");

		/// <summary>Expiração anterior ou igual à entrada no ar.</summary>
		public static readonly Error ExpiracaoInvalida =
			Error.Validation(
				"Intranet.Publicacao.ExpiracaoInvalida",
				"A data de expiração precisa ser posterior à data de entrada no ar.");

		/// <summary>
		/// Registro não encontrado, ou fora do alcance de quem pediu. Os dois casos
		/// compartilham o erro de propósito: distinguir revelaria a existência da publicação
		/// a quem não administra o setor.
		/// </summary>
		public static readonly Error NotFound =
			Error.NotFound("Intranet.Publicacao.NotFound", "Publicação não encontrada.");
	}

	/// <summary>Erros do recurso Inventário.</summary>
	public static class Inventario
	{
		/// <summary>Nome ausente ou vazio.</summary>
		public static readonly Error NomeRequired =
			Error.Validation("Intranet.Inventario.NomeRequired", "O nome é obrigatório.");

		/// <summary>Nome acima do limite configurado.</summary>
		public static Error NomeTooLong(int limit) =>
			Error.Validation("Intranet.Inventario.NomeTooLong", $"O nome excede o limite de {limit} caracteres.");

		/// <summary>Registro não encontrado no banco do tenant atual.</summary>
		public static readonly Error NotFound =
			Error.NotFound("Intranet.Inventario.NotFound", "Item de inventário não encontrado.");

		/// <summary>Tentativa de alterar um item já baixado — baixa é terminal.</summary>
		public static readonly Error ItemBaixado =
			Error.Validation(
				"Intranet.Inventario.ItemBaixado", "Um item baixado não aceita mais alterações.");

		/// <summary>Transição de status pedida não é válida a partir do status atual do item.</summary>
		public static readonly Error TransicaoInvalida =
			Error.Validation(
				"Intranet.Inventario.TransicaoInvalida",
				"Essa ação não é válida para o status atual do item.");

		/// <summary>Ação de atribuir sem um usuário informado.</summary>
		public static readonly Error UsuarioRequired =
			Error.Validation("Intranet.Inventario.UsuarioRequired", "Escolha um usuário para atribuir o item.");
	}

	/// <summary>Erros da gestão de acesso (perfis e usuários).</summary>
	public static class Acesso
	{
		/// <summary>SecureGate não configurado neste ambiente.</summary>
		public static readonly Error NaoConfigurado =
			Error.Unavailable(
				"Intranet.Acesso.NaoConfigurado",
				"O SecureGate não está configurado neste ambiente, então a gestão de acesso não está disponível.");

		/// <summary>SecureGate fora do ar, lento ou recusando — sem detalhe interno (ADR-0020).</summary>
		public static readonly Error Indisponivel =
			Error.Unavailable(
				"Intranet.Acesso.Indisponivel",
				"Não foi possível falar com o SecureGate agora. Tente novamente em instantes.");

		/// <summary>Perfil não informado.</summary>
		public static readonly Error PerfilRequired =
			Error.Validation("Intranet.Acesso.PerfilRequired", "Informe o perfil.");

		/// <summary>Nome fora da regra da plataforma.</summary>
		public static readonly Error PerfilNomeInvalido =
			Error.Validation(
				"Intranet.Acesso.PerfilNomeInvalido",
				"O nome do perfil aceita letras, dígitos, ponto, sublinhado e hífen, sem espaços, com até 100 caracteres.");

		/// <summary>Perfil inexistente no tenant.</summary>
		public static readonly Error PerfilNaoEncontrado =
			Error.NotFound("Intranet.Acesso.PerfilNaoEncontrado", "Perfil não encontrado.");

		/// <summary>Usuário inexistente no tenant.</summary>
		public static readonly Error UsuarioNaoEncontrado =
			Error.NotFound("Intranet.Acesso.UsuarioNaoEncontrado", "Usuário não encontrado.");

		/// <summary>Já existe um perfil com esse nome.</summary>
		public static readonly Error PerfilJaExiste =
			Error.Conflict("Intranet.Acesso.PerfilJaExiste", "Já existe um perfil com esse nome.");

		/// <summary>Perfil reservado da plataforma.</summary>
		public static readonly Error PerfilReservado =
			Error.Validation(
				"Intranet.Acesso.PerfilReservado",
				"Perfis reservados da plataforma não podem ser criados nem atribuídos por aqui.");

		/// <summary>Perfil do produto ou de setor não se exclui.</summary>
		public static readonly Error PerfilProtegido =
			Error.Validation(
				"Intranet.Acesso.PerfilProtegido",
				"Este perfil é do produto ou de um setor e não pode ser excluído. Para tirar um setor de uso, desative o setor.");

		/// <summary>Perfil com membros não se exclui.</summary>
		public static readonly Error PerfilComMembros =
			Error.Conflict("Intranet.Acesso.PerfilComMembros", "O perfil ainda tem membros. Retire todos antes de excluir.");

		/// <summary>Deixaria a instalação sem <c>intranet-admin</c> ativo.</summary>
		public static readonly Error UltimoIntranetAdmin =
			Error.Validation(
				"Intranet.Acesso.UltimoIntranetAdmin",
				"Este é o último intranet-admin ativo. Atribua o perfil a outra pessoa antes.");

		/// <summary>O admin tentou retirar o próprio <c>intranet-admin</c>.</summary>
		public static readonly Error AutoRemocaoDeIntranetAdmin =
			Error.Validation(
				"Intranet.Acesso.AutoRemocaoDeIntranetAdmin",
				"Você não pode retirar o seu próprio perfil intranet-admin. Peça a outro intranet-admin.");

		/// <summary>O admin tentou desativar a própria conta.</summary>
		public static readonly Error AutoDesativacao =
			Error.Validation(
				"Intranet.Acesso.AutoDesativacao",
				"Você não pode desativar a sua própria conta.");

		/// <summary>A plataforma recusou a desativação (ex.: último operador da instalação).</summary>
		public static readonly Error DesativacaoRecusada =
			Error.Conflict(
				"Intranet.Acesso.DesativacaoRecusada",
				"A plataforma recusou a desativação (por exemplo, o último operador da instalação).");
	}

	/// <summary>Erros do Diretório organizacional.</summary>
	public static class Diretorio
	{
		/// <summary>Usuário inexistente ou desativado — não aparece no diretório.</summary>
		public static readonly Error PessoaNaoEncontrada =
			Error.NotFound("Intranet.Diretorio.PessoaNaoEncontrada", "Pessoa não encontrada.");

		/// <summary>Nome acima do limite.</summary>
		public static Error NomeTooLong(int limite) =>
			Error.Validation("Intranet.Diretorio.NomeTooLong", $"O nome excede o limite de {limite} caracteres.");

		/// <summary>Cargo acima do limite.</summary>
		public static Error CargoTooLong(int limite) =>
			Error.Validation("Intranet.Diretorio.CargoTooLong", $"O cargo excede o limite de {limite} caracteres.");

		/// <summary>Ramal acima do limite.</summary>
		public static Error RamalTooLong(int limite) =>
			Error.Validation("Intranet.Diretorio.RamalTooLong", $"O ramal excede o limite de {limite} caracteres.");

		/// <summary>Texto "sobre" acima do limite.</summary>
		public static Error SobreTooLong(int limite) =>
			Error.Validation("Intranet.Diretorio.SobreTooLong", $"O texto \"sobre\" excede o limite de {limite} caracteres.");

		/// <summary>Setor inexistente, ou inativo para uma lotação nova.</summary>
		public static readonly Error SetorInvalido =
			Error.Validation("Intranet.Diretorio.SetorInvalido", "Escolha um setor existente e ativo.");

		/// <summary>Gestor que não é um usuário ativo do tenant.</summary>
		public static readonly Error GestorInvalido =
			Error.Validation("Intranet.Diretorio.GestorInvalido", "O gestor precisa ser um usuário ativo.");

		/// <summary>Autogestor.</summary>
		public static readonly Error GestorEhOProprio =
			Error.Validation("Intranet.Diretorio.GestorEhOProprio", "Ninguém pode ser gestor de si mesmo.");

		/// <summary>A escolha fecharia um ciclo de gestão.</summary>
		public static readonly Error GestorCriariaCiclo =
			Error.Validation(
				"Intranet.Diretorio.GestorCriariaCiclo",
				"Essa escolha faria a pessoa reportar, direta ou indiretamente, a si mesma.");
	}
}
