using Secco.Intranet.Application;
using Secco.Intranet.Infrastructure;
using Secco.Intranet.Web.Authentication;
using Secco.Intranet.Web.Tenancy;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Seeding;

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

// Cross-cutting individual do SDK (ADR-0004): correlação, tenancy (ADR-0005) e health
// checks. Sem AddSeccoAuthentication()/AddSeccoAuthorization() — ver comentário acima.
builder.Services.AddSeccoCorrelation();
builder.Services.AddSeccoTenancy();
builder.Services.AddSeccoHealthChecks();

builder.Services.AddIntranetApplication();
builder.Services.AddIntranetInfrastructure();
builder.Services.AddIntranetAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
	// ADR-0020: nunca expor stack trace/detalhe interno fora de Development.
	app.UseExceptionHandler("/Home/Error");
	app.UseHsts();
}

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
app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Home}/{action=Index}/{id?}");

if (app.Environment.IsDevelopment())
{
	// Migrations + seed automáticos SOMENTE em Development (ADR-0005/0019) — fora daqui,
	// aplicar migrations é processo controlado, nunca efeito colateral de startup.
	await app.Services.MigrateIntranetTenantDatabasesAsync();
	await app.Services.SeedSeccoDataAsync();
}

await app.RunAsync();

/// <summary>Ponto de entrada exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
