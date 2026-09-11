#requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $Version = '',
    [string] $Output = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\DyndDns.TrayApp\DyndDns.TrayApp.csproj'
$properties = @()

if ($Version) {
    # A release tag («v0.2.0») names the build, so the published executable reports the version it was released
    # under instead of the one written in the project file.
    $properties += "-p:Version=$($Version.TrimStart('v'))"
}

if (Test-Path -LiteralPath $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

dotnet publish $project -c $Configuration -r $Runtime -o $Output @properties

Write-Host "Published to $Output"
