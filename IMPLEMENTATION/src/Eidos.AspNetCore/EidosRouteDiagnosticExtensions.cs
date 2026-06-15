using Microsoft.Extensions.Logging;

namespace Eidos.AspNetCore;

public static class EidosRouteDiagnosticExtensions
{
    public static LogLevel ToLogLevel(this EidosRouteDiagnosticSeverity severity) => severity switch
    {
        EidosRouteDiagnosticSeverity.Error => LogLevel.Error,
        EidosRouteDiagnosticSeverity.Warning => LogLevel.Warning,
        _ => LogLevel.Debug
    };

    public static void LogDiagnostic(this ILogger logger, EidosRouteDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(diagnostic);

        var level = diagnostic.Severity.ToLogLevel();
        if (logger.IsEnabled(level))
        {
            logger.Log(level, "Eidos mapping {Severity}: {Message}", diagnostic.Severity, diagnostic.Message);
        }
    }
}
