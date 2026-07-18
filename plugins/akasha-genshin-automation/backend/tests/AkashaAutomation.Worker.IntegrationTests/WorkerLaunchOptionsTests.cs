using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;

namespace AkashaAutomation.Worker.IntegrationTests;

public class WorkerLaunchOptionsTests
{
    private const string ValidToken = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Parse_ShouldAcceptCompleteValidArguments()
    {
        var result = WorkerLaunchOptions.Parse(
        [
            "--pipe", "akasha.test-123",
            "--token", ValidToken,
            "--parent-pid", "42",
            "--protocol-version", CompanionProtocol.CurrentVersion.ToString(),
        ]);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Equal("akasha.test-123", result.Options!.PipeName);
        Assert.Equal(ValidToken, result.Options.Token);
        Assert.Equal(42, result.Options.ParentProcessId);
        Assert.Equal(CompanionProtocol.CurrentVersion, result.Options.ProtocolVersion);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Parse_ShouldRejectInvalidArguments(string[] arguments)
    {
        var result = WorkerLaunchOptions.Parse(arguments);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Options);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ParseErrors_ShouldNeverContainTheSessionToken()
    {
        var secret = "secret-token-that-must-not-be-logged";
        var result = WorkerLaunchOptions.Parse(
        [
            "--pipe", "valid.pipe",
            "--token", secret,
            "--parent-pid", "1",
            "--protocol-version", "99",
        ]);

        Assert.DoesNotContain(result.Errors, error => error.Contains(secret, StringComparison.Ordinal));
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        Array.Empty<string>(),
        new[] { "--pipe", "has\\slash", "--token", ValidToken, "--parent-pid", "1", "--protocol-version", "1" },
        new[] { "--pipe", "valid", "--token", "short", "--parent-pid", "1", "--protocol-version", "1" },
        new[] { "--pipe", "valid", "--token", ValidToken, "--parent-pid", "0", "--protocol-version", "1" },
        new[] { "--pipe", "valid", "--token", ValidToken, "--parent-pid", "1", "--protocol-version", "2" },
        new[] { "--pipe", "valid", "--pipe", "duplicate", "--token", ValidToken, "--parent-pid", "1", "--protocol-version", "1" },
        new[] { "--unknown", "value", "--pipe", "valid", "--token", ValidToken, "--parent-pid", "1", "--protocol-version", "1" },
    };
}
