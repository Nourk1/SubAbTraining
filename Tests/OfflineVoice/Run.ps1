param([string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Data')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$testOutput = Join-Path $env:TEMP ('patricia-protocol-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $testOutput | Out-Null
$runtime = Get-ChildItem "$UnityData\NetCoreRuntime\shared\Microsoft.NETCore.App" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$compiler = Get-ChildItem "$UnityData\DotNetSdk\sdk" -Recurse -Filter csc.dll | Select-Object -First 1
$json = Get-ChildItem "$projectRoot\Library\PackageCache\com.unity.nuget.newtonsoft-json*" -Recurse -Filter Newtonsoft.Json.dll | Select-Object -First 1
if (-not $runtime -or -not $compiler -or -not $json) { throw 'Import the project in Unity first, and pass the installed editor Data directory with -UnityData.' }
$arguments = @('-nologo', '-target:exe', '-nostdlib+', '-define:UNITY_EDITOR', ('-out:' + (Join-Path $testOutput 'ProtocolChecks.dll')))
$arguments += Get-ChildItem $runtime.FullName -Filter *.dll | ForEach-Object {
    try { [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; '-r:' + $_.FullName }
    catch { } # Native runtime libraries are not compiler references.
}
$arguments += '-r:' + $json.FullName
$arguments += @((Join-Path $projectRoot 'Assets\Scripts\OpenAIRealtimeClient.cs'), (Join-Path $projectRoot 'Assets\Scripts\ConversationTranscript.cs'), (Join-Path $projectRoot 'Assets\Scripts\ObjectiveEvidence.cs'), (Join-Path $PSScriptRoot 'ProtocolChecks.cs'))
$arguments += @((Join-Path $projectRoot 'Assets\Scripts\CoachingProtocol.cs'), (Join-Path $PSScriptRoot 'CoachingChecks.cs'))
$arguments += @((Join-Path $projectRoot 'Assets\Scripts\AssessmentProtocol.cs'), (Join-Path $PSScriptRoot 'AssessmentChecks.cs'))
$arguments += @((Join-Path $projectRoot 'Assets\Scripts\AttemptStore.cs'), (Join-Path $PSScriptRoot 'HistoryChecks.cs'))
$arguments += @((Join-Path $projectRoot 'Assets\Scripts\TrainingScenario.cs'), (Join-Path $PSScriptRoot 'ScenarioChecks.cs'))
$arguments += @((Join-Path $projectRoot 'Assets\Scripts\SessionControlState.cs'), (Join-Path $PSScriptRoot 'SessionControlChecks.cs'))
& "$UnityData\NetCoreRuntime\dotnet.exe" $compiler.FullName @arguments
if ($LASTEXITCODE -ne 0) { throw 'Protocol check compilation failed.' }
Copy-Item -LiteralPath $json.FullName -Destination (Join-Path $testOutput 'Newtonsoft.Json.dll')
@{ runtimeOptions = @{ tfm = 'net8.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name } } } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $testOutput 'ProtocolChecks.runtimeconfig.json')
& "$UnityData\NetCoreRuntime\dotnet.exe" (Join-Path $testOutput 'ProtocolChecks.dll') (Join-Path $projectRoot 'Assets\Resources\AssessmentRubric.json') (Join-Path $projectRoot 'Library\PatriciaOfflineTests') (Join-Path $projectRoot 'Assets\Resources\TrainingScenarios.json')
if ($LASTEXITCODE -ne 0) { throw 'Protocol checks failed.' }
