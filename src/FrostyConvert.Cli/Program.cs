using FrostyConvert.Core.Cli;

namespace FrostyConvert.Cli;

/// <summary>
/// Thin console host for development (<c>dotnet run --project src/FrostyConvert.Cli</c>).
/// Release packages ship a single dual-mode <c>Frosty Converter.exe</c> instead.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => CliRunner.Run(args);
}
