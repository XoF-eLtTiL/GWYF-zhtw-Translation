param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Release package not found: $PackagePath"
}

$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("gwyf-release-check-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null

try {
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $temporaryDirectory -Force
    $hasAutoTranslatorPlugin = [bool](Get-ChildItem -LiteralPath $temporaryDirectory -Recurse -File |
        Where-Object { $_.FullName -match 'XUnity\.AutoTranslator' } |
        Select-Object -First 1)

    $config = Get-ChildItem -LiteralPath $temporaryDirectory -Recurse -File -Filter "AutoTranslatorConfig.ini" |
        Select-Object -First 1

    if (-not $config) {
        if ($hasAutoTranslatorPlugin) {
            throw "AutoTranslatorConfig.ini was not found, but XUnity AutoTranslator is present in package: $PackagePath"
        }

        Write-Host "Release package verified: XUnity AutoTranslator is not present."
        return
    }

    $endpoint = Select-String -LiteralPath $config.FullName -Pattern '^Endpoint=' |
        Select-Object -First 1
    $fallbackEndpoint = Select-String -LiteralPath $config.FullName -Pattern '^FallbackEndpoint=' |
        Select-Object -First 1
    $overrideFontTextMeshPro = Select-String -LiteralPath $config.FullName -Pattern '^OverrideFontTextMeshPro=' |
        Select-Object -First 1
    $fallbackFontTextMeshPro = Select-String -LiteralPath $config.FullName -Pattern '^FallbackFontTextMeshPro=' |
        Select-Object -First 1

    if ($endpoint -and $endpoint.Line.Trim() -ne 'Endpoint=') {
        throw "Automatic translation endpoint must be disabled in release package. Found: $($endpoint.Line)"
    }

    if ($fallbackEndpoint -and $fallbackEndpoint.Line.Trim() -ne 'FallbackEndpoint=') {
        throw "Fallback translation endpoint must be disabled in release package. Found: $($fallbackEndpoint.Line)"
    }

    if ($overrideFontTextMeshPro -and $overrideFontTextMeshPro.Line.Trim() -ne 'OverrideFontTextMeshPro=') {
        throw "TMP font override must be disabled in release package. Found: $($overrideFontTextMeshPro.Line)"
    }

    if ($fallbackFontTextMeshPro -and $fallbackFontTextMeshPro.Line.Trim() -ne 'FallbackFontTextMeshPro=') {
        throw "TMP fallback font override must be disabled in release package. Found: $($fallbackFontTextMeshPro.Line)"
    }

    Write-Host "Release package verified: automatic translation endpoints and TMP font overrides are disabled."
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
}
