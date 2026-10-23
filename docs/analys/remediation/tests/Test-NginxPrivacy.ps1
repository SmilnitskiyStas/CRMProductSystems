param([string]$NginxImage = 'nginx:alpine')

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('phase-1-nginx-' + [Guid]::NewGuid().ToString('N'))
$marker = 'SYNTHETIC_BEARER_MUST_NOT_BE_LOGGED'
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$certificate = $null
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

function Invoke-IsolatedNginx([string[]]$DockerArguments) {
    $captured = (& docker @DockerArguments 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "Isolated nginx validation failed: $captured" }
    return $captured
}

try {
    # Synthetic certificate only: no deployment keys, secrets, traffic, or writable source mounts.
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=synthetic-nginx-test', $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddHours(1))
    [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'fullchain.pem'), $certificate.ExportCertificatePem())
    [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'privkey.pem'), $rsa.ExportPkcs8PrivateKeyPem())
    [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'chain.pem'), $certificate.ExportCertificatePem())
    [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'nginx.conf'),
        'error_log /dev/stderr; events {} http { include /etc/nginx/mime.types; include /fixture/rate.conf; include /fixture/site.conf; }')

    $common = @('run','--rm','--pull','never','--network','none','--read-only',
        '--tmpfs','/var/cache/nginx','--tmpfs','/var/run','--tmpfs','/var/log/nginx')
    $gatewayMounts = @('--mount', ('type=bind,source=' + (Join-Path $repositoryRoot 'infra/gateway/nginx.conf') + ',target=/etc/nginx/nginx.conf,readonly'))
    $gatewaySyntax = Invoke-IsolatedNginx ($common + $gatewayMounts + @('--entrypoint','nginx',$NginxImage,'-t'))
    # No upstream services exist. Force a hub failure to exercise nginx's raw error path too.
    $gatewayCommand = 'nginx && (wget -q -T 3 -O /dev/null "http://127.0.0.1/api/hubs/consumer-support?access_token=' + $marker + '" 2>/dev/null || true) && nginx -s quit'
    $gatewayLog = Invoke-IsolatedNginx ($common + $gatewayMounts + @('--entrypoint','sh',$NginxImage,'-c',$gatewayCommand))
    if ($gatewayLog.Contains($marker) -or $gatewayLog -notmatch 'GET /api/hubs/consumer-support HTTP/') {
        throw 'Gateway must retain a sanitized hub access record without the synthetic bearer marker.'
    }

    $edgeMounts = @('--mount', ('type=bind,source=' + (Join-Path $fixtureRoot 'nginx.conf') + ',target=/etc/nginx/nginx.conf,readonly'),
        '--mount', ('type=bind,source=' + $fixtureRoot + ',target=/etc/letsencrypt/live/agrusystems.pp.ua,readonly'),
        '--mount', ('type=bind,source=' + (Join-Path $repositoryRoot 'infra/nginx/shelfguard-ratelimit.conf') + ',target=/fixture/rate.conf,readonly'),
        '--mount', ('type=bind,source=' + (Join-Path $repositoryRoot 'infra/nginx/shelfguard.conf') + ',target=/fixture/site.conf,readonly'))
    $edgeSyntax = Invoke-IsolatedNginx ($common + $edgeMounts + @('--entrypoint','nginx',$NginxImage,'-t'))
    # The self-signed TLS certificate belongs exclusively to this disposable loopback fixture.
    $edgeCommand = 'nginx && (wget -q --no-check-certificate -T 3 --header "Host: api.agrusystems.pp.ua" -O /dev/null "https://127.0.0.1:8443/api/hubs/consumer-support?access_token=' + $marker + '" 2>/dev/null || true) && nginx -s quit && cat /var/log/nginx/access.log'
    $edgeLog = Invoke-IsolatedNginx ($common + $edgeMounts + @('--entrypoint','sh',$NginxImage,'-c',$edgeCommand))
    if ($edgeLog.Contains($marker) -or $edgeLog -notmatch 'GET /api/hubs/consumer-support HTTP/') {
        throw 'Edge must retain a sanitized hub access record without the synthetic bearer marker.'
    }

    $result = [ordered]@{ gateway_syntax = 'PASS'; gateway_hub_failure_log_privacy = 'PASS';
        edge_syntax = 'PASS'; edge_hub_failure_log_privacy = 'PASS';
        environment = 'disposable containers; no network or published ports; synthetic loopback requests only';
        image = $NginxImage; edge_existing_http2_deprecation_warning = $edgeSyntax.Contains('deprecated') }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot '../test-results/phase-1-nginx.json')
    $result | ConvertTo-Json
}
finally {
    if ($certificate) { $certificate.Dispose() }
    $rsa.Dispose()
    if (-not [System.IO.Path]::GetFullPath($fixtureRoot).StartsWith(
        [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unexpected fixture cleanup path'
    }
    foreach ($name in @('fullchain.pem','privkey.pem','chain.pem','nginx.conf')) {
        Remove-Item -LiteralPath (Join-Path $fixtureRoot $name) -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $fixtureRoot
}
