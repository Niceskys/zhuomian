$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$required = @(
    'README.md',
    'AGENTS.md',
    'CONTRIBUTING.md',
    'SECURITY.md',
    'docs/DEVELOPMENT_PLAN.md',
    'docs/PRODUCT_SPEC.md',
    'docs/INTERACTION_SPEC.md',
    'docs/ARCHITECTURE.md',
    'docs/PERFORMANCE_BUDGET.md',
    'docs/RELIABILITY_TEST_PLAN.md',
    'docs/DIAGNOSTICS.md',
    'docs/PHASE_0_STATUS.md',
    'docs/ROADMAP.md',
    'docs/AUDIT_CONSOLIDATION.md',
    'docs/ADR/README.md',
    'docs/ADR/TEMPLATE.md'
)

$errors = [System.Collections.Generic.List[string]]::new()

foreach ($relative in $required) {
    $path = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("Missing required file: $relative")
    }
}

$markdownFiles = Get-ChildItem -LiteralPath $repoRoot -Filter '*.md' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' }

$linkPattern = [regex]'\[[^\]]+\]\((?<target>[^)]+)\)'

foreach ($file in $markdownFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($content -match "`t") {
        $errors.Add("Tab character found: $($file.FullName.Substring($repoRoot.Length + 1))")
    }

    foreach ($match in $linkPattern.Matches($content)) {
        $target = $match.Groups['target'].Value.Trim()
        if ($target -match '^(https?://|mailto:|#)') { continue }

        $pathPart = ($target -split '#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
        $decoded = [Uri]::UnescapeDataString($pathPart)
        $resolved = Join-Path $file.DirectoryName $decoded
        if (-not (Test-Path -LiteralPath $resolved)) {
            $relativeFile = $file.FullName.Substring($repoRoot.Length + 1)
            $errors.Add("Broken local link in ${relativeFile}: $target")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Validated $($markdownFiles.Count) Markdown files and $($required.Count) required files."
