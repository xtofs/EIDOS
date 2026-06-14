using System.Diagnostics;
using System.Text.Json.Serialization;
using Eidos.AspNetCore;
using Eidos.Sample.HumanResources;
using Microsoft.AspNetCore.WebUtilities;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // configure JSON options globally to support PATCH requests with JsonPatchDocument<T> payloads, and to serialize enums as strings in OpenAPI docs
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.AddEidosOpenApiAndReDoc(
            HumanResourcesService.ParseSchema(),
            new Eidos.Core.OpenApi.ApiInfo("Eidos HR Sample", "0.1"));

        builder.Logging.AddConsole();

        // register the HR repository (SQLite by default; see appsettings 'Hr:Provider' / 'ConnectionStrings:HrDb')
        builder.UseHumanResourcesRepository();

        // ////////////////////////
        // Web Application configuration

        var app = builder.Build();
        app.Use(SimpleHttpLoggingMiddleware);

        app.MapEidosOpenApiAndReDoc();

        // create schema + seed sample data once at startup
        app.Services.GetRequiredService<IHumanResourcesRepository>().Initialize();

        // wire up the HR API
        app.MapHrEndpoints();

        app.Run();

    }

    private static async Task SimpleHttpLoggingMiddleware(HttpContext context, Func<Task> next)
    {
        var sw = Stopwatch.StartNew();
        await next();
        sw.Stop();

        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RequestSummary");

        logger.LogInformation(
            "{method} {path}{query} => {status} {reason} in {elapsed}ms",
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString,
            context.Response.StatusCode,
            ReasonPhrases.GetReasonPhrase(context.Response.StatusCode),
            sw.ElapsedMilliseconds);
    }
}