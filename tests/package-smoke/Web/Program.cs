// ASP.NET Core shared framework: its Microsoft.Extensions.* assemblies are older than the ones
// the packages are built against, so the packages' own dependencies must win at runtime.
var builder = WebApplication.CreateBuilder(args);
return Smoke.Run(builder.Services);
