#requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $Output = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\DyndDns.TrayApp\DyndDns.TrayApp.csproj'

if (Test-Path -LiteralPath $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

dotnet publish $project -c $Configuration -r $Runtime -o $Output

Write-Host "Published to $Output"
