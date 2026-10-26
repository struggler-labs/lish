# LISH 빌드 → dist\lish.exe (의존 DLL 내장 단일 실행 파일)
# 사용: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = "$root\src\Lish\Lish.csproj"
$out = "$root\src\Lish\bin\Release\net48"
$ico = "$root\src\Lish\assets\lish.ico"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe" }

function Build {
    & $dotnet build $proj -c Release -nologo
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
}

Build

# 앱 아이콘이 없으면 앱이 직접 그린 아이콘으로 생성 후 다시 빌드
if (-not (Test-Path $ico)) {
    New-Item -ItemType Directory -Force (Split-Path $ico) | Out-Null
    $p = Start-Process "$out\lish.exe" -ArgumentList "--make-icon", "`"$ico`"" -Wait -PassThru
    if ($p.ExitCode -ne 0 -or -not (Test-Path $ico)) { throw "아이콘 생성 실패" }
    Build
}

$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$out\lish.exe" $dist -Force
Copy-Item "$out\lish.exe.config" $dist -Force
# 내장 글꼴 라이선스(SIL OFL 1.1)는 배포물에 함께 넣어야 한다.
Copy-Item "$root\src\Lish\assets\fonts\OFL.txt" "$dist\D2Coding-OFL.txt" -Force
Write-Host "완료: $dist\lish.exe" -ForegroundColor Green
