<#
.SYNOPSIS
    Verifica se o ambiente está pronto para rodar a CLI do gmud-automation contra o Azure real.
    Nunca imprime valores de segredos — apenas se cada variável está definida.
.EXAMPLE
    ./preflight.ps1 -Organization minha-org -NeedsAppService
#>
param(
    [Parameter(Mandatory)] [string] $Organization,
    # Informe para etapas que leem o App Service (release-branch)
    [switch] $NeedsAppService
)

$ok = $true
function Report([string] $name, [bool] $passed, [string] $hint) {
    if ($passed) { Write-Host "[OK]    $name" }
    else { Write-Host "[FALTA] $name — $hint"; $script:ok = $false }
}

# Mesma regra de ResolvePatEnvironmentVariableName em Program.cs
$defaultOrganization = 'eleven11C'
$patVariable = if ($Organization -ieq $defaultOrganization) { 'AZURE_DEVOPS_EXT_PAT' }
               else { 'AZURE_DEVOPS_PAT_' + ($Organization.ToUpperInvariant() -replace '[^A-Z0-9]', '_') }

Report "dotnet SDK" ([bool](Get-Command dotnet -ErrorAction SilentlyContinue)) "instale o .NET SDK 10"
Report "PAT ($patVariable)" (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($patVariable))) "carregue o .env local"

$catalog = $env:GMUD_REPOSITORY_CATALOG_PATH
if ([string]::IsNullOrWhiteSpace($catalog)) {
    Write-Host "[AVISO] GMUD_REPOSITORY_CATALOG_PATH não definida — será usado o catálogo FICTÍCIO de exemplo"
} else {
    Report "Catálogo de repositórios" (Test-Path $catalog) "o arquivo apontado não existe"
}

if ($env:GMUD_KNOWN_ORGANIZATION -and $Organization -ieq $env:GMUD_KNOWN_ORGANIZATION -and -not $env:GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT) {
    Write-Host "[AVISO] Organização conhecida sem GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT — Repos usará o projeto de Boards"
}

if ($NeedsAppService) {
    Report "AZURE_SUBSCRIPTION_ID" (-not [string]::IsNullOrWhiteSpace($env:AZURE_SUBSCRIPTION_ID)) "defina ou use --subscription-id"
    $azOk = $false
    if (Get-Command az -ErrorAction SilentlyContinue) {
        az account show --only-show-errors 1>$null 2>$null
        $azOk = ($LASTEXITCODE -eq 0)
    }
    Report "Azure CLI autenticado (az login)" $azOk "rode 'az login'"
}

if ($ok) { Write-Host "Preflight OK."; exit 0 } else { Write-Host "Preflight falhou."; exit 1 }
