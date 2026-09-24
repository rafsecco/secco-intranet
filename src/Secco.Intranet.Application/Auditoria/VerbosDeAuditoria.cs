namespace Secco.Intranet.Application.Auditoria;

/// <summary>
/// Verbos canônicos da trilha. Constantes, e não texto solto no handler: o verbo é o que
/// alguém vai filtrar daqui a um ano, e um erro de digitação some da busca sem avisar.
/// </summary>
public static class VerbosDeAuditoria
{
	/// <summary>Publicação criada.</summary>
	public const string MuralPublicar = "mural.publicar";

	/// <summary>Conteúdo, agendamento, visibilidade ou prioridade alterados.</summary>
	public const string MuralEditar = "mural.editar";

	/// <summary>Publicação tirada de circulação.</summary>
	public const string MuralArquivar = "mural.arquivar";

	/// <summary>Arquivo enviado.</summary>
	public const string DocumentoPublicar = "documento.publicar";

	/// <summary>Documento tirado de circulação.</summary>
	public const string DocumentoArquivar = "documento.arquivar";

	/// <summary>Setor criado — provisiona as Roles no SecureGate (ADR-0001).</summary>
	public const string SetorCriar = "setor.criar";

	/// <summary>Nome ou ícone do setor alterados.</summary>
	public const string SetorEditar = "setor.editar";

	/// <summary>Setor passou a inativo — muda quem enxerga o quê.</summary>
	public const string SetorDesativar = "setor.desativar";

	/// <summary>Setor voltou a ativo.</summary>
	public const string SetorReativar = "setor.reativar";

	/// <summary>Item de inventário criado.</summary>
	public const string InventarioCriar = "inventario.criar";

	/// <summary>Campos descritivos do item alterados.</summary>
	public const string InventarioEditar = "inventario.editar";

	/// <summary>Item atribuído a um usuário.</summary>
	public const string InventarioAtribuir = "inventario.atribuir";

	/// <summary>Item baixado.</summary>
	public const string InventarioBaixar = "inventario.baixar";

	/// <summary>Perfil criado no SecureGate.</summary>
	public const string AcessoPerfilCriar = "acesso.perfil-criar";

	/// <summary>Perfil excluído.</summary>
	public const string AcessoPerfilExcluir = "acesso.perfil-excluir";

	/// <summary>Perfil atribuído a um usuário.</summary>
	public const string AcessoPerfilAtribuir = "acesso.perfil-atribuir";

	/// <summary>Perfil retirado de um usuário.</summary>
	public const string AcessoPerfilRetirar = "acesso.perfil-retirar";

	/// <summary>Conta de usuário desativada.</summary>
	public const string AcessoUsuarioDesativar = "acesso.usuario-desativar";

	/// <summary>Conta de usuário reativada.</summary>
	public const string AcessoUsuarioReativar = "acesso.usuario-reativar";

	/// <summary>Sessões de um usuário encerradas.</summary>
	public const string AcessoSessoesEncerrar = "acesso.sessoes-encerrar";
}

/// <summary>Tipos de recurso da trilha.</summary>
public static class RecursosDeAuditoria
{
	/// <summary>Publicação do mural.</summary>
	public const string Publicacao = "publicacao";

	/// <summary>Documento de setor.</summary>
	public const string Documento = "documento";

	/// <summary>Setor.</summary>
	public const string Setor = "setor";

	/// <summary>Item de inventário.</summary>
	public const string Inventario = "inventario";

	/// <summary>Perfil ou usuário do SecureGate, na gestão de acesso.</summary>
	public const string Acesso = "acesso";
}
