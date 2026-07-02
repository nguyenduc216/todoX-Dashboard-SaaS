# ==============================================
# Test Google Vertex AI Imagen permissions
# Save as: D:\Test-Vertex-Imagen-With-Your-SA.ps1
# Requirement: Copy your service account JSON to D:\todox-vertex-sa.json
# ==============================================

$ProjectId = "gen-lang-client-0004868688"
$Location = "us-central1"
$ServiceAccountJsonPath = "D:\todox-vertex-sa.json"

$Prompt = "A cute blue robot accountant working in a modern office, ultra realistic, high quality"
$OutputFolder = "D:\VertexImagenTest"
$OutputImage = Join-Path $OutputFolder "vertex-image.png"
$ResponseFile = Join-Path $OutputFolder "vertex-image-response.json"
$ErrorFile = Join-Path $OutputFolder "vertex-image-error.txt"
$TokenInfoFile = Join-Path $OutputFolder "vertex-token-info.txt"

# Try Imagen models. The script stops at the first successful model.
$ModelsToTry = @(
    "imagen-4.0-generate-preview-06-06",
    "imagen-3.0-generate-002"
)

function Base64UrlEncode($bytes) {
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-AccessTokenFromServiceAccount {
    param($JsonPath)

    if (!(Test-Path $JsonPath)) {
        throw "Service account JSON not found: $JsonPath"
    }

    $sa = Get-Content $JsonPath -Raw | ConvertFrom-Json

    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $exp = $now + 3600

    $header = @{
        alg = "RS256"
        typ = "JWT"
    } | ConvertTo-Json -Compress

    $claim = @{
        iss = $sa.client_email
        scope = "https://www.googleapis.com/auth/cloud-platform"
        aud = "https://oauth2.googleapis.com/token"
        iat = $now
        exp = $exp
    } | ConvertTo-Json -Compress

    $header64 = Base64UrlEncode([Text.Encoding]::UTF8.GetBytes($header))
    $claim64 = Base64UrlEncode([Text.Encoding]::UTF8.GetBytes($claim))
    $unsignedJwt = "$header64.$claim64"

    $privateKeyPem = $sa.private_key
    $privateKeyPem = $privateKeyPem -replace "-----BEGIN PRIVATE KEY-----", ""
    $privateKeyPem = $privateKeyPem -replace "-----END PRIVATE KEY-----", ""
    $privateKeyPem = $privateKeyPem -replace "\s", ""

    $privateKeyBytes = [Convert]::FromBase64String($privateKeyPem)

    $rsa = [System.Security.Cryptography.RSA]::Create()
    $bytesRead = 0
    $rsa.ImportPkcs8PrivateKey($privateKeyBytes, [ref]$bytesRead)

    $signatureBytes = $rsa.SignData(
        [Text.Encoding]::UTF8.GetBytes($unsignedJwt),
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1
    )

    $signature64 = Base64UrlEncode($signatureBytes)
    $jwt = "$unsignedJwt.$signature64"

    $tokenBody = @{
        grant_type = "urn:ietf:params:oauth:grant-type:jwt-bearer"
        assertion = $jwt
    }

    $tokenResponse = Invoke-RestMethod `
        -Uri "https://oauth2.googleapis.com/token" `
        -Method Post `
        -Body $tokenBody `
        -ContentType "application/x-www-form-urlencoded"

    return @{
        access_token = $tokenResponse.access_token
        client_email = $sa.client_email
        project_id = $sa.project_id
    }
}

function Write-ApiError {
    param($Exception, $FilePath)

    $message = ""
    $message += "Exception: $($Exception.Exception.Message)`r`n"

    if ($Exception.Exception.Response) {
        try {
            $stream = $Exception.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $body = $reader.ReadToEnd()
            $message += "Response Body:`r`n$body`r`n"
        }
        catch {
            $message += "Could not read response body.`r`n"
        }
    }

    $message | Out-File $FilePath -Encoding utf8
}

try {
    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null

    Write-Host ""
    Write-Host "Vertex Imagen permission test"
    Write-Host "ProjectId: $ProjectId"
    Write-Host "Location : $Location"
    Write-Host "SA file  : $ServiceAccountJsonPath"
    Write-Host "Output   : $OutputFolder"
    Write-Host ""

    Write-Host "Getting Google access token..."
    $tokenInfo = Get-AccessTokenFromServiceAccount -JsonPath $ServiceAccountJsonPath
    $AccessToken = $tokenInfo.access_token

    "Client email: $($tokenInfo.client_email)`r`nProject ID from JSON: $($tokenInfo.project_id)`r`nConfigured Project ID: $ProjectId" | Out-File $TokenInfoFile -Encoding utf8

    Write-Host "Access token OK."
    Write-Host "Service account: $($tokenInfo.client_email)"
    Write-Host ""

    foreach ($Model in $ModelsToTry) {
        Write-Host "Testing model: $Model"

        $Url = "https://$Location-aiplatform.googleapis.com/v1/projects/$ProjectId/locations/$Location/publishers/google/models/$Model:predict"

        $Body = @{
            instances = @(
                @{
                    prompt = $Prompt
                }
            )
            parameters = @{
                sampleCount = 1
                aspectRatio = "1:1"
            }
        } | ConvertTo-Json -Depth 20

        try {
            $Response = Invoke-RestMethod `
                -Uri $Url `
                -Method Post `
                -Headers @{
                    Authorization = "Bearer $AccessToken"
                    "Content-Type" = "application/json"
                } `
                -Body $Body

            $Response | ConvertTo-Json -Depth 30 | Out-File $ResponseFile -Encoding utf8

            $Base64Image = $null

            if ($Response.predictions[0].bytesBase64Encoded) {
                $Base64Image = $Response.predictions[0].bytesBase64Encoded
            }
            elseif ($Response.predictions[0].image.bytesBase64Encoded) {
                $Base64Image = $Response.predictions[0].image.bytesBase64Encoded
            }

            if ($Base64Image) {
                [IO.File]::WriteAllBytes($OutputImage, [Convert]::FromBase64String($Base64Image))

                Write-Host ""
                Write-Host "SUCCESS"
                Write-Host "Model used    : $Model"
                Write-Host "Image saved   : $OutputImage"
                Write-Host "Response saved: $ResponseFile"
                exit 0
            }
            else {
                Write-Host "No base64 image found. Response saved: $ResponseFile"
            }
        }
        catch {
            Write-Host "Model failed: $Model"
            Write-ApiError -Exception $_ -FilePath $ErrorFile
            Write-Host "Error saved: $ErrorFile"
            Write-Host ""
        }
    }

    Write-Host "All models failed. Please send me: $ErrorFile"
    exit 1
}
catch {
    Write-ApiError -Exception $_ -FilePath $ErrorFile
    Write-Host "FAILED"
    Write-Host "See: $ErrorFile"
    exit 1
}
