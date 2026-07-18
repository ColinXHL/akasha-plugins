[CmdletBinding()]
param(
    [string]$NavigatorRepository,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$backendRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$pluginTemplate = [IO.Path]::GetFullPath((Join-Path $backendRoot ".."))
$pluginsRepository = [IO.Path]::GetFullPath(
    (Join-Path $backendRoot "..\..\.."))
if ([string]::IsNullOrWhiteSpace($NavigatorRepository)) {
    $NavigatorRepository = Join-Path $pluginsRepository "..\AkashaNavigator"
}
$navigatorRepository = [IO.Path]::GetFullPath($NavigatorRepository)
$navigatorProject = Join-Path $navigatorRepository "AkashaNavigator\AkashaNavigator.csproj"
$workerProject = Join-Path $backendRoot "src\AkashaAutomation.Worker\AkashaAutomation.Worker.csproj"
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("akasha-companion-smoke-" + [Guid]::NewGuid().ToString("N"))
$pluginRoot = Join-Path $temporaryRoot "plugin"
$workerOutput = Join-Path $pluginRoot "runtime"
$harnessRoot = Join-Path $temporaryRoot "harness"

if (-not (Test-Path -LiteralPath $navigatorProject -PathType Leaf)) {
    throw "AkashaNavigator project was not found: $navigatorProject"
}

try {
    New-Item -ItemType Directory -Path $workerOutput, $harnessRoot -Force | Out-Null
    $sourceManifest = Get-Content `
        -LiteralPath (Join-Path $pluginTemplate "manifest.json") `
        -Raw |
        ConvertFrom-Json
    $runtimeManifest = [ordered]@{
        id = [string]$sourceManifest.id
        name = [string]$sourceManifest.name
        version = [string]$sourceManifest.version
        main = [string]$sourceManifest.main
        settings = [string]$sourceManifest.settings
        description = [string]$sourceManifest.description
        permissions = @($sourceManifest.permissions)
        profiles = @($sourceManifest.profiles)
        library = @($sourceManifest.library)
        httpAllowedUrls = @($sourceManifest.httpAllowedUrls)
        defaultConfig = $sourceManifest.defaultConfig
        companion = [ordered]@{
            executable = [string]$sourceManifest.backend.entry
            protocolVersion = [int]$sourceManifest.backend.protocolVersion
            lifetime = [string]$sourceManifest.backend.lifetime
            singleInstance = $true
            integrityLevel = [string]$sourceManifest.backend.integrityLevel
            shutdownTimeoutMs = [int]$sourceManifest.backend.shutdownTimeoutMs
        }
    }
    $runtimeManifest |
        ConvertTo-Json -Depth 12 |
        Set-Content `
            -LiteralPath (Join-Path $pluginRoot "plugin.json") `
            -Encoding utf8NoBOM
    Copy-Item `
        -LiteralPath (Join-Path $pluginTemplate "frontend") `
        -Destination (Join-Path $pluginRoot "frontend") `
        -Recurse
    Copy-Item -LiteralPath (Join-Path $pluginTemplate "README.md") -Destination $pluginRoot

    dotnet publish $workerProject `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained false `
        --output $workerOutput `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Worker publish failed with exit code $LASTEXITCODE."
    }

    $escapedNavigatorProject = [Security.SecurityElement]::Escape($navigatorProject)
    $projectFile = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$escapedNavigatorProject" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $harnessRoot "CompanionSmoke.csproj") -Value $projectFile -Encoding utf8

    $programFile = @'
using System.Text.Json;
using AkashaNavigator.Models.Plugin;
using AkashaNavigator.Services;

if (args.Length != 1)
{
    throw new ArgumentException("Expected the temporary plugin root as the only argument.");
}

var pluginRoot = Path.GetFullPath(args[0]);
var manifestResult = PluginManifest.LoadFromFile(Path.Combine(pluginRoot, "plugin.json"));
if (!manifestResult.IsSuccess || manifestResult.Manifest?.Companion == null)
{
    throw new InvalidDataException($"Plugin manifest validation failed: {manifestResult.ErrorMessage}");
}

var pluginId = manifestResult.Manifest.Id!;
var manifest = manifestResult.Manifest.Companion;

using var manager = new CompanionProcessManager(new LogService(Path.Combine(pluginRoot, "logs")));
var first = await manager.StartAsync(pluginId, pluginRoot, manifest);
if (!first.Running || first.ProcessId is null)
{
    throw new InvalidOperationException("The companion did not reach the running state.");
}

var second = await manager.StartAsync(pluginId, pluginRoot, manifest);
if (second.ProcessId != first.ProcessId)
{
    throw new InvalidOperationException("Single-instance start created a second process.");
}

var request = JsonSerializer.SerializeToElement(new { message = "navigator-worker-echo" });
var response = await manager.InvokeAsync(pluginId, "worker.echo", request);
if (response is null || response.Value.GetProperty("message").GetString() != "navigator-worker-echo")
{
    throw new InvalidOperationException("Echo response did not match the request.");
}

var workerStatus = await manager.InvokeAsync(pluginId, "worker.getStatus", null);
if (workerStatus is null ||
    workerStatus.Value.GetProperty("state").GetString() != "ready" ||
    workerStatus.Value.GetProperty("gameWindow").GetProperty("state").GetString() != "not_found" ||
    !workerStatus.Value.GetProperty("realInputEnabled").GetBoolean())
{
    throw new InvalidOperationException("Worker status did not describe the foreground-only no-window Ready state.");
}

var autoPickOptions = JsonSerializer.SerializeToElement(new
{
    enabled = true,
    pickKey = "E",
    blackListEnabled = true,
    userExactBlacklist = new[] { "测试精确项" },
});
var autoDialogueOptions = JsonSerializer.SerializeToElement(new
{
    enabled = true,
    interactionKey = "E",
    optionStrategy = "Last",
    advanceKey = "Interaction",
    beforeAdvanceDelayMilliseconds = 100,
    afterOptionDelayMilliseconds = 200,
    closePopupPagesEnabled = false,
});
var appliedAutoPick = await manager.InvokeAsync(pluginId, "features.autoPick.setOptions", autoPickOptions);
var appliedAutoDialogue = await manager.InvokeAsync(pluginId, "features.autoDialogue.setOptions", autoDialogueOptions);
if (appliedAutoPick is null || appliedAutoPick.Value.GetProperty("pickKey").GetString() != "E" ||
    appliedAutoDialogue is null || appliedAutoDialogue.Value.GetProperty("optionStrategy").GetString() != "Last" ||
    appliedAutoDialogue.Value.GetProperty("afterOptionDelayMilliseconds").GetInt32() != 200)
{
    throw new InvalidOperationException("Feature options were not applied by the Worker.");
}

var enabledStatus = await manager.InvokeAsync(pluginId, "worker.getStatus", null);
if (enabledStatus is null ||
    !enabledStatus.Value.GetProperty("features").GetProperty("autoPick").GetProperty("isEnabled").GetBoolean() ||
    !enabledStatus.Value.GetProperty("features").GetProperty("autoDialogue").GetProperty("isEnabled").GetBoolean())
{
    throw new InvalidOperationException("Feature switches were not applied by the Worker.");
}

var disabledPayload = JsonSerializer.SerializeToElement(new { enabled = false });
await manager.InvokeAsync(pluginId, "features.autoPick.setEnabled", disabledPayload);
await manager.InvokeAsync(pluginId, "features.autoDialogue.setEnabled", disabledPayload);

await manager.StopAsync(pluginId);
if (manager.GetStatus(pluginId).Running)
{
    throw new InvalidOperationException("The companion was still running after stop.");
}

Console.WriteLine($"PASS process={first.ProcessId} echo=navigator-worker-echo status=ready/no-window input=foreground-only options=true switches=true stopped=true");
'@
    Set-Content -LiteralPath (Join-Path $harnessRoot "Program.cs") -Value $programFile -Encoding utf8

    dotnet run `
        --project (Join-Path $harnessRoot "CompanionSmoke.csproj") `
        --configuration $Configuration `
        --no-launch-profile `
        -- $pluginRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Companion smoke test failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
