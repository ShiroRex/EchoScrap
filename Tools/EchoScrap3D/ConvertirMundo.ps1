# Ejemplo:
# .\Tools\EchoScrap3D\ConvertirMundo.ps1 -Fuente "C:\Users\USER\Downloads\Contenido_Coleccionista_mundo"
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Fuente,
    [string]$Blender = ""
)
$ErrorActionPreference = 'Stop'
$Proyecto = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path (Join-Path $Proyecto 'Assets\_Project\Scenes\CombatArena.unity'))) {
    throw "No encuentro CombatArena. Pon este ZIP dentro de la carpeta Echo-juego existente, no en otro proyecto."
}
if (-not (Test-Path $Fuente)) { throw "Carpeta de modelos no encontrada: $Fuente" }
$Fuentes = @(Get-ChildItem -Path $Fuente -Filter '*.blend' -Recurse -File)
if ($Fuentes.Count -eq 0) { throw "No se encontraron .blend en: $Fuente. Extrae primero el RAR usando WinRAR o 7-Zip." }
if ([string]::IsNullOrWhiteSpace($Blender)) {
    $comando = Get-Command blender -ErrorAction SilentlyContinue
    if ($comando) { $Blender = $comando.Source }
    else {
        $candidatos = @(Get-ChildItem 'C:\Program Files\Blender Foundation' -Filter 'blender.exe' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName -Descending)
        if ($candidatos.Count -gt 0) { $Blender = $candidatos[0].FullName }
    }
}
if (-not $Blender -or -not (Test-Path $Blender)) {
    throw 'Blender no está instalado o no se encuentra. Instálalo, o usa -Blender "C:\ruta\blender.exe".'
}
$Script = Join-Path $PSScriptRoot 'exportar_blend_a_fbx.py'
$Salida = Join-Path $Proyecto 'Assets\_Project\Art\Mundo3D\Modelos'
$Logs = Join-Path $Proyecto 'Tools\EchoScrap3D\Logs'
New-Item -ItemType Directory -Force -Path $Salida,$Logs | Out-Null
$ok = 0; $fallidos = @()
foreach ($archivo in $Fuentes) {
    $fbx = Join-Path $Salida ($archivo.BaseName + '.fbx')
    $log = Join-Path $Logs ($archivo.BaseName + '.log')
    Write-Host ("Convirtiendo {0} -> {1}" -f $archivo.Name, (Split-Path $fbx -Leaf)) -ForegroundColor Cyan
    & $Blender -b -t 2 --python $Script -- $archivo.FullName $fbx *> $log
    if ($LASTEXITCODE -eq 0 -and (Test-Path $fbx) -and ((Get-Item $fbx).Length -gt 1024)) {
        $ok++
        Write-Host '  OK' -ForegroundColor Green
    } else {
        $fallidos += $archivo.Name
        Write-Warning "Falló $($archivo.Name). Revisa $log"
    }
}
Write-Host "Terminados: $ok de $($Fuentes.Count). Archivos FBX en $Salida" -ForegroundColor Green
if ($fallidos.Count -gt 0) {
    Write-Warning ('Fallidos: ' + ($fallidos -join ', '))
    Write-Host 'Puedes seguir importando el resto y corregir esos modelos después.'
}
Write-Host 'Vuelve a Unity, espera a que termine de importar y usa EchoScrap > Arte 3D > 1 - Crear mapa de vertedero.'
