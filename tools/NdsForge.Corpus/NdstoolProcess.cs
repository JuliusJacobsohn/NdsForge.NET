using System.Diagnostics;

namespace NdsForge.Corpus;

/// <summary>Runs the historical executable with argument-list escaping and captures complete reproducible console evidence.</summary>
internal static class NdstoolProcess
{
    /// <summary>Executes one bounded operation without interpreting nonzero status, since legacy failures are oracle evidence too.</summary>
    /// <param name="executable">Verified ndstool executable path.</param>
    /// <param name="workingDirectory">Private disposable workspace receiving any outputs.</param>
    /// <param name="name">Stable operation identity written into the oracle document.</param>
    /// <param name="arguments">Exact arguments passed without shell parsing.</param>
    /// <param name="recordedArguments">Portable argument representation, usually replacing the source path with <c>{ROM}</c>.</param>
    /// <returns>Exit status, duration, complete output, and an initially empty artifact list.</returns>
    public static async Task<OracleOperation> RunAsync(
        string executable,
        string workingDirectory,
        string name,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string>? recordedArguments = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new IOException($"Could not start {executable}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        stopwatch.Stop();
        return new(
            name,
            recordedArguments ?? arguments,
            process.ExitCode,
            stopwatch.ElapsedMilliseconds,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false),
            []);
    }
}
