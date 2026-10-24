param([ValidateSet('Recovery', 'Sessions', 'Matrix', 'Http')][string]$Suite = 'Recovery')
$ErrorActionPreference = 'Stop'
# Fail before creating any resources when the engine is unavailable or still starting.
# Kill only this read-only child probe after a bounded wait; never restart/reset Docker.
$probe = [System.Diagnostics.Process]::new()
$probe.StartInfo = [System.Diagnostics.ProcessStartInfo]::new('docker')
$probe.StartInfo.ArgumentList.Add('version')
$probe.StartInfo.ArgumentList.Add('--format')
$probe.StartInfo.ArgumentList.Add('{{.Server.Version}}')
$probe.StartInfo.UseShellExecute = $false
$probe.StartInfo.CreateNoWindow = $true
$probe.StartInfo.RedirectStandardOutput = $true
$probe.StartInfo.RedirectStandardError = $true
try {
    if (-not $probe.Start()) { throw 'Could not start the Docker readiness probe.' }
    if (-not $probe.WaitForExit(15000)) {
        $probe.Kill()
        $probe.WaitForExit()
        throw 'Docker engine did not respond within 15 seconds; no test fixture was started.'
    }
    if ($probe.ExitCode -ne 0) { throw 'Docker engine is not ready; no test fixture was started.' }
}
finally { $probe.Dispose() }
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$fixtureId = [Guid]::NewGuid().ToString('N')
$networkName = 'recovery-test-network-' + $fixtureId
$containerName = 'recovery-test-db-' + $fixtureId
if (Test-Path Env:SHELFGUARD_RECOVERY_TEST_DB) {
    throw 'An existing recovery test connection is configured; this fixture will not read or overwrite it.'
}
$fixturePassword = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$testExitCode = 1
try {
    # Docker internal networks on this host do not publish loopback ports. Use a dedicated
    # fixture bridge with outbound masquerading disabled and no other attached services.
    & docker network create --driver bridge --opt com.docker.network.bridge.enable_ip_masquerade=false $networkName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create a disposable recovery-only test network.' }
    & docker run -d --rm --pull never --name $containerName --network $networkName --cpus 1 --memory 512m `
        --tmpfs /var/lib/postgresql/data:rw -p '127.0.0.1::5432' `
        --env POSTGRES_DB=recovery_test --env POSTGRES_USER=recovery_fixture --env ("POSTGRES_PASSWORD=" + $fixturePassword) `
        postgres:16-alpine | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the disposable recovery database.' }
    $portBinding = (& docker port $containerName '5432/tcp' | Out-String).Trim()
    if ($portBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Recovery fixture must bind only to a random loopback port.' }
    $fixturePort = $Matches[1]
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        # The image's temporary initialization server accepts Unix-socket probes before
        # POSTGRES_DB exists. Verify a real TCP query against the final database instead.
        & docker exec --env ("PGPASSWORD=" + $fixturePassword) $containerName psql -h 127.0.0.1 -U recovery_fixture -d recovery_test -Atc 'SELECT 1' 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'Disposable PostgreSQL did not become ready.' }
    & docker exec $containerName psql -U recovery_fixture -d recovery_test -v ON_ERROR_STOP=1 -c 'CREATE EXTENSION IF NOT EXISTS pg_trgm;' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the application-required pg_trgm extension in the disposable fixture.' }
    & docker exec $containerName psql -U recovery_fixture -d recovery_test -v ON_ERROR_STOP=1 -c "CREATE COLLATION public.uk_cyrl_first (provider = icu, locale = 'uk-u-kr-cyrl-latn', deterministic = true);" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the application-required Ukrainian collation in the disposable fixture.' }
    $env:SHELFGUARD_RECOVERY_TEST_DB = "Host=127.0.0.1;Port=$fixturePort;Database=recovery_test;Username=recovery_fixture;Password=$fixturePassword;Pooling=false"
    $testFilter = if ($Suite -eq 'Http') { 'FullyQualifiedName~SecurityHttpAcceptanceTests' } elseif ($Suite -eq 'Matrix') { 'FullyQualifiedName~PosConcurrencySalesIntegrationTests|FullyQualifiedName~LoyaltyConcurrencySalesIntegrationTests|FullyQualifiedName~AccessMatrixPostgresTests|FullyQualifiedName~SessionPostgresTests|FullyQualifiedName~PasswordRecoveryPostgresTests' } elseif ($Suite -eq 'Sessions') { 'FullyQualifiedName~SessionPostgresTests|FullyQualifiedName~PasswordRecoveryPostgresTests' } else { 'FullyQualifiedName~PasswordRecoveryPostgresTests' }
    $testReport = if ($Suite -eq 'Http') { 'trx;LogFileName=phase-1-http-acceptance.trx' } elseif ($Suite -eq 'Matrix') { 'trx;LogFileName=phase-1-access-matrix-postgres.trx' } elseif ($Suite -eq 'Sessions') { 'trx;LogFileName=phase-1-sessions-postgres.trx' } else { 'trx;LogFileName=phase-1-password-recovery-postgres.trx' }
    & dotnet test (Join-Path $repositoryRoot 'backend/ShelfGuard.Tests/ShelfGuard.Tests.csproj') --no-build --no-restore `
        --filter $testFilter --logger 'console;verbosity=minimal' `
        --logger $testReport `
        --results-directory (Join-Path $PSScriptRoot '../test-results')
    $testExitCode = $LASTEXITCODE
}
finally {
    Remove-Item -LiteralPath Env:SHELFGUARD_RECOVERY_TEST_DB -ErrorAction SilentlyContinue
    # Only the randomly named fixture resources created above may be removed.
    if ($containerName -ne ('recovery-test-db-' + $fixtureId) -or $networkName -ne ('recovery-test-network-' + $fixtureId)) {
        throw 'Unexpected recovery fixture cleanup identity.'
    }
    & docker rm --force $containerName 2>&1 | Out-Null
    & docker network rm $networkName 2>&1 | Out-Null
}
exit $testExitCode
