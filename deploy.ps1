# ============================================================
# deploy.ps1 - Deploy backend lên server srpg-backend.duckdns.org
# Dùng: .\deploy.ps1
# ============================================================

param(
    [string]$SshUser     = "ubuntu",
    [string]$SshHost     = "srpg-backend.duckdns.org",
    [string]$SshKeyPath  = "$HOME\.ssh\id_rsa",
    [string]$ProjectPath = "/opt/srpg-backend",
    [string]$ServiceName = "srpg-backend"
)

Write-Host "=== Deploy SRPG Backend ===" -ForegroundColor Cyan
Write-Host "Host  : $SshUser@$SshHost"
Write-Host "Path  : $ProjectPath"
Write-Host "Svc   : $ServiceName"
Write-Host ""

$remoteScript = @"
set -e
echo '--- Git pull ---'
cd $ProjectPath
git pull origin main

echo '--- Dotnet build ---'
dotnet build --configuration Release --no-restore

echo '--- Restart service ---'
sudo systemctl restart $ServiceName
sudo systemctl status $ServiceName --no-pager
echo '--- Done ---'
"@

ssh -i $SshKeyPath -o StrictHostKeyChecking=no "$SshUser@$SshHost" $remoteScript

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[OK] Deploy thanh cong!" -ForegroundColor Green
} else {
    Write-Host "`n[FAIL] Deploy that bai, kiem tra SSH / service." -ForegroundColor Red
    exit 1
}
