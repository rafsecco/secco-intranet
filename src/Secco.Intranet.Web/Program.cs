using Secco.Intranet.Application;
using Secco.Intranet.Infrastructure;
using Secco.Intranet.Web.Tenancy;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.EntityFrameworkCore.Seeding;

// Raiz de composição do monolito (ADR-0002): Secco.Intranet.Web (MVC) consome a
// Application layer diretamente, em processo — sem uma Api HTTP separada. Hoje NÃO há
// autenticação/autorização registrada: a integração OIDC via Secco.SecureGate (relying
// party) é item futuro do roadmap (ver docs/roadmap.md, Fase 0). Por isso as extensões
// individuais do SDK são usadas em vez de AddSeccoPlatform()/UseSeccoPlatform() — essas
// exigem a seção Secco:Authentication (fail-fast) porque assumem um resource server JWT,
// o que este host de usuário humano ainda não é.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Cross-cutting individual do SDK (ADR-0004): correlação, tenancy (ADR-0005) e health
// checks. Sem AddSeccoAuthentication()/AddSeccoAuthorization() — ver comentário acima.
builder.Services.AddSeccoCorrelation();
builder.Services.AddSeccoTenancy();
builder.Services.AddSeccoHealthChecks();

builder.Services.AddIntranetApplication();
builder.Services.AddIntranetInfrastructure();

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

if (app.Environment.IsDevelopment())
{
	// Suprimento interino de tenant para navegação manual em DEV (ADR-0020: nunca em
	// produção — ver comentário na própria classe). Precisa vir antes de
	// UseSeccoTenancy(), que é quem efetivamente resolve o TenantContext do escopo.
	app.UseMiddleware<DevelopmentTenantMiddleware>();
}

app.UseSeccoTenancy();

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
