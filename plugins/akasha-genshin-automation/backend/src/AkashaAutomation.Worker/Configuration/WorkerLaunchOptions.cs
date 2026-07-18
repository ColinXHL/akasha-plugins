using System.Text.RegularExpressions;
using AkashaAutomation.Worker.Bridge;

namespace AkashaAutomation.Worker.Configuration;

public sealed partial record WorkerLaunchOptions(
    string PipeName,
    string Token,
    int ParentProcessId,
    int ProtocolVersion)
{
    private static readonly string[] RequiredNames =
    [
        "--pipe",
        "--token",
        "--parent-pid",
        "--protocol-version",
    ];

    public static WorkerLaunchOptionsParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var errors = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            if (!RequiredNames.Contains(name, StringComparer.Ordinal))
            {
                errors.Add($"Unknown argument '{name}'.");
                continue;
            }

            if (values.ContainsKey(name))
            {
                errors.Add($"Argument '{name}' was supplied more than once.");
                if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    index++;
                }

                continue;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"Argument '{name}' requires a value.");
                continue;
            }

            values.Add(name, args[++index]);
        }

        foreach (var requiredName in RequiredNames)
        {
            if (!values.ContainsKey(requiredName))
            {
                errors.Add($"Required argument '{requiredName}' is missing.");
            }
        }

        if (values.TryGetValue("--pipe", out var pipeName) && !PipeNamePattern().IsMatch(pipeName))
        {
            errors.Add("Pipe name must contain 1-128 ASCII letters, digits, '.', '_' or '-'.");
        }

        if (values.TryGetValue("--token", out var token) &&
            (token.Length is < 32 or > 512 || token.Any(char.IsWhiteSpace)))
        {
            errors.Add("Session token must contain 32-512 non-whitespace characters.");
        }

        var parentProcessId = 0;
        if (values.TryGetValue("--parent-pid", out var parentProcessIdText) &&
            (!int.TryParse(parentProcessIdText, out parentProcessId) || parentProcessId <= 0))
        {
            errors.Add("Parent PID must be a positive integer.");
        }

        var protocolVersion = 0;
        if (values.TryGetValue("--protocol-version", out var protocolVersionText) &&
            (!int.TryParse(protocolVersionText, out protocolVersion) ||
             protocolVersion != CompanionProtocol.CurrentVersion))
        {
            errors.Add($"Protocol version must be {CompanionProtocol.CurrentVersion}.");
        }

        if (errors.Count > 0)
        {
            return new WorkerLaunchOptionsParseResult(null, errors);
        }

        return new WorkerLaunchOptionsParseResult(
            new WorkerLaunchOptions(pipeName!, token!, parentProcessId, protocolVersion),
            []);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex PipeNamePattern();
}

public sealed record WorkerLaunchOptionsParseResult(
    WorkerLaunchOptions? Options,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Options is not null && Errors.Count == 0;
}
