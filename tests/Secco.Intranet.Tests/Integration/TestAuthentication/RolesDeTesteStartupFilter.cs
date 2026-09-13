using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Secco.Intranet.Tests.Integration.TestAuthentication;

/// <summary>Insere <see cref="RolesDeTesteMiddleware"/> no início do pipeline de teste.</summary>
internal sealed class RolesDeTesteStartupFilter : IStartupFilter
{
	public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
		app =>
		{
			app.UseMiddleware<RolesDeTesteMiddleware>();
			next(app);
		};
}
