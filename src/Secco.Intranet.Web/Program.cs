using Secco.Intranet.Application;
using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Infrastructure;
using Secco.Intranet.Web;
using Secco.Intranet.Web.Auditoria;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Conteudo;
using Secco.Intranet.Web.Hosting;
using Secco.Intranet.Web.Navigation;
using Secco.Intranet.Web.Tenancy;
using Secco.Intranet.Web.Theming;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Secco.SDK.Logging;

// Raiz de composição do monolito (ADR-0002): Secco.Intranet.Web (MVC) consome a
// Application layer diretamente, em processo — sem uma Api HTTP separada. A autenticação é
// um relying party OIDC contra o Secco.SecureGate (ADR-0023), registrada de forma LAZY por
// configuração via AddIntranetAuthentication() — presente a seção Secco:SecureGate:Authority,
// vira relying party de verdade; ausente, segue no modo aberto de DEV local/Testing (ver
// Secco.Intranet.Web.Authentication.IntranetAuthenticationExtensions). Por isso as extensões
// individuais do SDK são usadas em vez de AddSeccoPlatform()/UseSeccoPlatform() — essas
// exigem a seção Secco:Authentication (fail-fast) porque assumem um resource server JWT, e a
// Intranet é um cliente humano (cookie de sessão), não um resource server.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// A página do setor mora na raiz (/{slug}/…): a restrição recusa os slugs reservados.
builder.Services.Configure<RouteOptions>(opcoes =>
	opcoes.ConstraintMap[SlugDeSetorRouteConstraint.Nome] = typeof(SlugDeSetorRouteConstraint));

// Sistema de temas (ADR-0003/ADR-0004): o expander da precedencia as views do tema ativo.
builder.Services.AddIntranetTheming();
// Itens extras do menu principal (versoes de pacote: ADR-0011): o catalogo le as application
// parts no primeiro uso, entao uma RCL externa so precisa estar referenciada.
builder.Services.AddSingleton<CatalogoDeItensDoMenuPrincipal>();
builder.Services.AddSingleton<IRenderizadorMarkdown, RenderizadorMarkdown>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAtorAtual, AtorDoHttpContext>();
builder.Services.AddScoped<IPermissoesDeSetor, PermissoesDeSetor>();
builder.Services.AddScoped<AcessoAoRecursoDoSetor>();

// Cross-cutting individual do SDK (ADR-0004): correlação, tenancy (ADR-0005) e health
// checks. Sem AddSeccoAuthentication() — ver comentário acima (autorização por permissão,
// AddSeccoAuthorization(), é registrada abaixo, incondicional).
builder.Services.AddSeccoCorrelation();
builder.Services.AddSeccoTenancy();
builder.Services.AddSeccoHealthChecks();

// Sink ILogger -> LogStream (ADR-0006): presente a seção Secco:LogStream, o log vai para lá;
// ausente, o ILogger local segue sozinho.
builder.Services.AddLogStream(opcoes =>
{
    opcoes.ServiceName = "secco-intranet";

    // O Enabled do pacote vem true por padrão, e aí o validador passa a exigir BaseUrl,
    // credenciais e scope — sem a seção, a aplicação nem subiria. Desligar aqui é o que
    // torna o envio opcional de verdade.
    if (string.IsNullOrWhiteSpace(opcoes.BaseUrl))
    {
        opcoes.Enabled = false;
    }
});

builder.Services.AddIntranetApplication();
builder.Services.AddIntranetInfrastructure(builder.Configuration);
builder.Services.AddIntranetAuthentication(builder.Configuration, builder.Environment);

// Secco:SecureGate:Authority ausente derruba o startup em Production, nao so deixa os
// controllers caírem no modo aberto de Development sem ninguém notar (ADR-0020).
builder.Services.AddHostedService<ValidacaoSecureGateHostedService>();

// ADR-0021: autorização por permissão, incondicional — o ambiente Testing não configura
// SecureGate, mas precisa da policy dinâmica rodando de verdade (mesmo padrão dos gates por nome
// de Role de hoje, com claims falsas e autorização real). Sem SecureGate real, o próprio
// AddSeccoAuthorization() já registra um resolvedor por configuração (nega tudo por padrão).
builder.Services.AddSeccoAuthorization();

// Confiança em X-Forwarded-For/X-Forwarded-Proto, opt-in por configuração (ver a classe): só
// habilite atrás de um reverse proxy que seja o único caminho até a aplicação.
builder.Services.AddIntranetProxyForwarding(builder.Configuration);

var app = builder.Build();

// Processo controlado (ADR-0005; ADR-0038 da plataforma): `dotnet Secco.Intranet.Web.dll migrate`
// aplica as migrations de todos os tenants do catálogo e o seed de referência, e sai sem subir o
// servidor. O deploy o executa antes de subir a versão nova.
if (SeccoCommands.IsMigrate(args))
{
	return await app.Services.RunSeccoMigrationsAsync() ? 0 : 1;
}

if (!app.Environment.IsDevelopment())
{
	// ADR-0020: nunca expor stack trace/detalhe interno fora de Development.
	app.UseExceptionHandler("/Home/Error");
	app.UseHsts();
}

// Antes de qualquer middleware que olhe esquema/IP da requisição (ADR-0020) — se a chave não
// estiver ligada, isto não faz nada (ForwardedHeaders.None é o padrão do framework).
app.UseForwardedHeaders();

app.UseStatusCodePages();
app.UseStaticFiles();
app.UseRouting();

app.UseSeccoCorrelation();

if (IntranetAuthenticationExtensions.IsConfigured(app.Configuration))
{
	app.UseAuthentication();
}

if (app.Environment.IsDevelopment())
{
	// Suprimento interino de tenant para navegação manual em DEV (ADR-0020: nunca em
	// produção — ver comentário na própria classe). Precisa vir antes de
	// UseSeccoTenancy(), que é quem efetivamente resolve o TenantContext do escopo. Com a
	// autenticação configurada, a claim tenant_id normalmente já vem do cookie — este
	// middleware vira no-op nesse caso (só age quando a requisição não carrega a claim).
	app.UseMiddleware<DevelopmentTenantMiddleware>();
}

app.UseSeccoTenancy();

if (IntranetAuthenticationExtensions.IsConfigured(app.Configuration))
{
	app.UseAuthorization();
}

app.MapSeccoHealthChecks();

// O navegador pede /favicon.ico na raiz quando a resposta não declara ícone (a foto servida
// direto, as páginas de erro em texto puro). O ícone é do tema — cada um tem a sua cor —, então a
// raiz aponta para o SVG do tema ativo, sem exigir login: senão o ícone viraria desafio de login.
app.MapGet("/favicon.ico", (HttpContext http, ThemeOptions tema) =>
		Results.Redirect($"{http.Request.PathBase}/_content/Secco.Intranet.Themes.{tema.Nome}/favicon.svg"))
	.AllowAnonymous()
	.ExcludeFromDescription();

app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Mural}/{action=Index}/{id?}");

if (app.Environment.IsDevelopment())
{
	// Em DEV, o mesmo código do verbo migrate: F5 continua automático. Fora daqui, o tenant novo
	// migra no primeiro uso, e o resto é o verbo, nunca efeito colateral de startup.
	await app.Services.RunSeccoMigrationsAsync();
}

await app.RunAsync();
return 0;

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
