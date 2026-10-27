param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$vfxRepo=(Resolve-Path -LiteralPath "$PSScriptRoot/../..").Path
$vfxQa=[IO.Path]::GetFullPath((Join-Path $vfxRepo 'Logs/UpgradeCardsQA/Project'))
if($vfxQa -ne (Join-Path $vfxRepo 'Logs/UpgradeCardsQA/Project')){throw 'Unexpected authoring project'}
if((Get-Item -LiteralPath $vfxQa).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Authoring project is a reparse point'}
$vfxRunning=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {$_.CommandLine -like "*$vfxQa*"}
if($vfxRunning){throw 'The isolated Unity project is already running'}
foreach($vfxFolder in @('Assets','Packages','ProjectSettings')){
 $vfxTarget=[IO.Path]::GetFullPath((Join-Path $vfxQa $vfxFolder))
 if(-not $vfxTarget.StartsWith($vfxQa+[IO.Path]::DirectorySeparatorChar) -or ((Get-Item -LiteralPath $vfxTarget).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unsafe authoring mirror'}
 robocopy (Join-Path $vfxRepo $vfxFolder) $vfxTarget /MIR /XJ /R:0 /W:0 /NFL /NDL /NJH /NJS /NP "/LOG:$vfxRepo/Logs/UpgradeVfxAnimated/Sync-$vfxFolder.log" | Out-Null
 if($LASTEXITCODE -ge 8){throw 'Authoring mirror failed'}
}
$vfxEditor=Join-Path $vfxQa 'Assets/Editor'
New-Item -ItemType Directory -Path $vfxEditor -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BakeUpgradeVfx.cs') -Destination (Join-Path $vfxEditor 'EchoScrapVfxBake.cs')
$vfxSettings=Join-Path $vfxQa 'ProjectSettings/ProjectSettings.asset'
$vfxSettingsText=[IO.File]::ReadAllText($vfxSettings)
$vfxSettingsText=[regex]::Replace($vfxSettingsText,'(?m)^  companyName:.*$','  companyName: EchoScrapLocalQA')
$vfxSettingsText=[regex]::Replace($vfxSettingsText,'(?m)^  productName:.*$','  productName: EchoScrapVfxQA')
[IO.File]::WriteAllText($vfxSettings,$vfxSettingsText)
$vfxArgs=@('-batchmode','-projectPath',('"'+$vfxQa+'"'),'-executeMethod','EchoScrap.Tools.BakeUpgradeVfx.Bake','-quit','-logFile',('"'+$vfxRepo+'/Logs/UpgradeVfxAnimated/Bake.log"'))
$vfxProcess=Start-Process -FilePath $Unity -ArgumentList $vfxArgs -WindowStyle Hidden -PassThru
Write-Output ('Baking editable prefabs in isolated Unity, PID '+$vfxProcess.Id)
$vfxProcess.WaitForExit()
if($vfxProcess.ExitCode -ne 0){throw 'Unity authoring failed; inspect Logs/UpgradeVfxAnimated/Bake.log'}
$vfxResource='Assets/_Project/Resources/Upgrades/Vfx'
foreach($vfxFolder in @('Materials','Prefabs')){
 $vfxSource=Join-Path $vfxQa "$vfxResource/$vfxFolder"
 if(-not (Test-Path -LiteralPath $vfxSource)){throw "Missing authored $vfxFolder"}
 Copy-Item -LiteralPath ($vfxSource+'.meta') -Destination (Join-Path $vfxRepo "$vfxResource/$vfxFolder.meta")
 New-Item -ItemType Directory -Path (Join-Path $vfxRepo "$vfxResource/$vfxFolder") -Force | Out-Null
 Get-ChildItem -LiteralPath $vfxSource -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $vfxRepo "$vfxResource/$vfxFolder/$($_.Name)")}
}
Get-ChildItem -LiteralPath (Join-Path $vfxQa "$vfxResource/Animated") -Filter '*.meta' -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $vfxRepo "$vfxResource/Animated/$($_.Name)")}
Write-Output 'Animated imports, editable materials and prefabs copied back.'
