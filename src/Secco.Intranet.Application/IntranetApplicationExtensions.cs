using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Application.Inventario;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Publicacoes;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Application.Vencimentos;

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
		services.AddScoped<EditarPermissoesDoPerfilHandler>();
		services.AddScoped<ReconciliarPermissoesHandler>();

		services.AddScoped<CriarItemMenuHandler>();
		services.AddScoped<AtivarDesativarItemMenuHandler>();
		services.AddScoped<ExcluirItemMenuHandler>();
		services.AddScoped<MoverItemMenuHandler>();
		services.AddScoped<ObterArvoreDeMenuHandler>();
		services.AddScoped<ReconciliarItensDeMenuHandler>();
		services.AddScoped<ResolverCaminhoDeMenuHandler>();
		services.AddScoped<ListarArvoresDosSetoresHandler>();

		services.AddScoped<ListarPessoasHandler>();
		services.AddScoped<ObterPessoaHandler>();
		services.AddScoped<EditarContatoHandler>();
		services.AddScoped<EditarDadosFuncionaisHandler>();
		services.AddScoped<ObterPessoaParaEdicaoHandler>();
		services.AddScoped<MontarOrganogramaHandler>();
		services.AddScoped<ImportarDiretorioHandler>();
		services.AddScoped<FotoDoColaboradorHandler>();

		// "Hoje" do produto é a data no fuso configurado; o relógio é singleton porque o fuso é resolvido uma vez.
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<RelogioDeVencimentos>();
		services.AddScoped<CriarVencimentoHandler>();
		services.AddScoped<EditarVencimentoHandler>();
		services.AddScoped<RenovarVencimentoHandler>();
		services.AddScoped<EncerrarVencimentoHandler>();
		services.AddScoped<ReativarVencimentoHandler>();
		services.AddScoped<ListarVencimentosDoSetorHandler>();
		services.AddScoped<ObterVencimentoHandler>();
		services.AddScoped<ProcessarAvisosDeVencimentoHandler>();

		return services;
	}
}
