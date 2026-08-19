using System.Diagnostics;
using System.Text;

namespace ShelfGuard.Tools.PchilkaImport.Source;

/// <summary>
/// Runs SELECT-only queries against the Pchilka POS MySQL export via `docker exec` into the
/// container's local Unix socket (mysql CLI, -N -B batch/tab-separated output) instead of a
/// direct TCP connection.
///
/// Why not TCP: this tool originally connected over 127.0.0.1:3307 with MySqlConnector, as the
/// TASK-513 brief describes. That failed with an incomplete-handshake error. Investigation
/// found `SHOW VARIABLES LIKE 'port'` returns 0 and `skip_networking=1` inside the container —
/// confirmed to persist across a full `docker restart`, so not a transient boot-order fluke.
/// MySQL's official docker-entrypoint.sh forces skip_networking on whenever --skip-grant-tables
/// is passed, regardless of any other networking flag given alongside it — a deliberate,
/// hardcoded guard against exposing a completely unauthenticated server on the network. This
/// tool does not attempt to weaken or route around that guard (that would be a security-setting
/// change, out of bounds for an unattended import script). It uses the access path the image
/// itself intends for --skip-grant-tables mode instead: the container-local Unix socket, via
/// `docker exec`. Every query issued through here is still SELECT-only — same constraint as the
/// brief's original "never write to it" rule, just reached over a different transport.
/// </summary>
public static class PchilkaCliClient
{
    private const string ContainerName = "pchilka-pos-mysql";
    private const string DatabaseName = "pchilka_pos_analytics";

    public static async Task<List<string[]>> QueryAsync(string sql, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // ArgumentList passes each element straight to CreateProcess's argv — no shell
        // involved, so the raw SQL text (which may itself contain quotes/newlines/Cyrillic)
        // never needs escaping and can't be reinterpreted by a shell.
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(ContainerName);
        psi.ArgumentList.Add("mysql");
        psi.ArgumentList.Add("-uroot");
        psi.ArgumentList.Add("--default-character-set=utf8mb4");
        psi.ArgumentList.Add("-N"); // no column-name header row
        psi.ArgumentList.Add("-B"); // batch mode: tab-separated, no box-drawing
        psi.ArgumentList.Add(DatabaseName);
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(sql);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'docker exec' process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"docker exec mysql failed (exit {process.ExitCode}): {stderr}");

        var rows = new List<string[]>();
        using var sr = new StringReader(stdout);
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            rows.Add(line.Split('\t'));
        }
        return rows;
    }

    /// <summary>Reads a cell as a nullable string — mysql -B prints the literal text "NULL" for SQL NULL.</summary>
    public static string? Cell(string[] row, int i) => i < row.Length && row[i] != "NULL" ? row[i] : null;
}
