using Serilog;

namespace RadioLogger.Services
{
    /// <summary>
    /// Helper estático para obtener loggers con contexto de clase.
    /// Uso: private static readonly ILogger _log = AppLog.For&lt;MiClase&gt;();
    /// </summary>
    public static class AppLog
    {
        public static ILogger For<T>() => Log.ForContext<T>();
        public static ILogger For(string context) => Log.ForContext("SourceContext", context);
    }
}
