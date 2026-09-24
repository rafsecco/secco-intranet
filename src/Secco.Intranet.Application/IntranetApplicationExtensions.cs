using Microsoft.Extensions.DependencyInjection;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;

namespace Secco.Intranet.Application;

/// <summary>Composição de DI da camada de aplicação.</summary>
public static class IntranetApplicationExtensions
{
	/// <summary>
	/// Registra os casos de uso. As options são registradas pela Infrastructure
	/// (bind lazy da configuração) — a Application não conhece configuração.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddIntranetApplication(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddScoped<CreateSetorHandler>();
		services.AddScoped<EditarSetorHandler>();
		services.AddScoped<GetSetorByIdHandler>();
		services.AddScoped<GetSetorBySlugHandler>();
		services.AddScoped<SearchSetoresHandler>();

		services.AddScoped<PublicarDocumentoHandler>();
		services.AddScoped<ListarDocumentosHandler>();
		services.AddScoped<BaixarDocumentoHandler>();
		services.AddScoped<ArquivarDocumentoHandler>();

		services.AddScoped<ListarMuralHandler>();
		services.AddScoped<ObterPublicacaoHandler>();
		services.AddScoped<PublicarPublicacaoHandler>();
		services.AddScoped<EditarPublicacaoHandler>();
		services.AddScoped<ArquivarPublicacaoHandler>();
		services.AddScoped<ListarPublicacoesDoSetorHandler>();

		services.AddScoped<CriarItemInventarioHandler>();
		services.AddScoped<EditarItemInventarioHandler>();
		services.AddScoped<MudarStatusItemInventarioHandler>();
		services.AddScoped<SearchItensInventarioHandler>();
		services.AddScoped<GetItemInventarioByIdHandler>();

		services.AddScoped<ListarPerfisHandler>();
		services.AddScoped<ObterPerfilHandler>();
		services.AddScoped<ListarUsuariosHandler>();
		services.AddScoped<ObterUsuarioHandler>();
		services.AddScoped<CriarPerfilHandler>();
		services.AddScoped<ExcluirPerfilHandler>();
		services.AddScoped<AtribuirPerfilHandler>();
		services.AddScoped<RetirarPerfilHandler>();
		services.AddScoped<DesativarUsuarioHandler>();
		services.AddScoped<ReativarUsuarioHandler>();
		services.AddScoped<EncerrarSessoesHandler>();

		services.AddScoped<ListarPessoasHandler>();
		services.AddScoped<ObterPessoaHandler>();
		services.AddScoped<EditarContatoHandler>();
		services.AddScoped<EditarDadosFuncionaisHandler>();
		services.AddScoped<ObterPessoaParaEdicaoHandler>();
		services.AddScoped<MontarOrganogramaHandler>();
		services.AddScoped<ImportarDiretorioHandler>();

		return services;
	}
}
