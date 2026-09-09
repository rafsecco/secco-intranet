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
}
