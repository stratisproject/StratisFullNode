Write-Host STRAX MultiSig Masternode Launch Script - Version 5 - InterFlux Initial -ForegroundColor Cyan
""
Start-Sleep 5

#Required Variables
$ethDataDir = Read-Host -Prompt "Please enter the path to your Ethereum datadir (if you are using default data directory please press ENTER)"
if ( $ethDataDir -eq $null ) { $ethDataDir = "$env:LOCALAPPDATA\Ethereum\ropsten" }
if ( -not ( Test-Path $ethDataDir\geth ) )
{
    ""
    $ethDataDir = Read-Host -Prompt "I could not find a data directory in the location you specified ($ethDataDir\geth). If you are performing a new synchronization, please re-enter the path to your Ethereum datadir to confirm. Alternatively, enter the correct path for the Ethereum data directory."
}   
Clear-Host
$ethPassword = Read-Host "Please Enter the Passphrase used to Protect the Ethereum Account"
Clear-Host
$multiSigMnemonic = ( Read-Host "Please Enter your 12 Mnemonic Words")
Clear-Host
$multiSigPassword = ( Read-Host "Please Enter the Passphrase used to Protect the MultiSig Wallet" )
Clear-Host
$multiSigPublicKey = ( Read-Host "Please Enter your Masternode Signing Key" )
$federationIPs = "167.86.74.136,206.220.197.50,217.160.46.215,5.158.81.18,13.90.213.188,165.173.0.136,27.72.97.81,217.7.52.43,104.237.202.7,3.94.72.119,93.114.128.47,139.99.237.65,51.195.100.33,167.88.12.75"
$mainChainAPIPort = '17103'
$sideChainAPIPort = '37223'
$gethAPIPort = "8545"
$ethMultiSigContract = "0x7a49ba62943a8a3eb590b2e27734d4cc67f878ba"
$ethWrappedStraxContract = "0xebc6d09f44c9133bc0fc0d47411cadd5230c0225"
$redeemScript = "8 02ba8b842997ce50c8e29c24a5452de5482f1584ae79778950b7bae24d4cc68dad 0337e816a3433c71c4bbc095a54a0715a6da7a70526d2afb8dba3d8d78d33053bf 032e4088451c5a7952fb6a862cdad27ea18b2e12bccb718f13c9fdcc1caf0535b4 035569e42835e25c854daa7de77c20f1009119a5667494664a46b5154db7ee768a 02d371f3a0cffffcf5636e6d4b79d9f018a1a18fbf64c39542b382c622b19af9de 02b3e16d2e4bbad6dba1e699934a52d58d9b60b6e7eed303e400e95f2dbc2ef3fd 0209cfca2490dec022f097114090c919e85047de0790c1c97451e0f50c2199a957 02387a219b1de54d4dc73a710a2315d957fc37ab04052a6e225c89205b90a881cd 035bf78614171397b080c5b375dbb7a5ed2a4e6fb43a69083267c880f66de5a4f9 02cbd907b0bf4d757dee7ea4c28e63e46af19dc8df0c924ee5570d9457be2f4c73 03797a2047f84ba7dcdd2816d4feba45ae70a59b3aa97f46f7877df61aa9f06a21 03cda7ea577e8fbe5d45b851910ec4a795e5cc12d498cf80d39ba1d9a455942188 02f891910d28fc26f272da8d7f548fdc18c286704907673e839dc07e8df416c15e 028078c0613033e5b4d4745300ede15d87ed339e379daadc6481d87abcb78732fa 02680321118bce869933b07ea42cc04d2a2804134b06db582427d6b9688b3536a4 f OP_CHECKMULTISIG"
$sidechainMasternodesRepo = "https://github.com/stratisproject/StratisFullNode.git"
$sidechainMasternodeBranch = "interfluxtest"
$stratisMasternodeDashboardRepo = "https://github.com/stratisproject/StratisMasternodeDashboard"

#Create Functions
function Get-TimeStamp 
{
    return "[{0:dd/MM/yy} {0:HH:mm:ss}]" -f (Get-Date)
}

#Check for pre-requisites
$dotnetVersion = dotnet --version
if ( -not ($dotnetVersion -gt "3.1.*") ) 
{
    Write-Host "ERROR:  .NET Core 3.1 SDK or above not found" -ForegroundColor Red
    Start-Sleep 30
    Exit
}

$gitVersion = git --version
if ( -not ($gitVersion -ne $null) ) 
{
    Write-Host "ERROR:  git not found" -ForegroundColor Red
    Start-Sleep 30
    Exit
}

$pwshVersion = $PSVersionTable.PSVersion.ToString()
if ( -not ($pwshVersion -gt "7.1") ) 
{
    Write-Host "ERROR:  PowerShell Core not found" -ForegroundColor Red
    Start-Sleep 30
    Exit
}   

$gethVersion = (geth version | ForEach-Object { if ( $_ -Like "Version:*" ) { $_ } })
if ( $gethVersion -eq $null )
{
    Write-Host "ERROR:  GETH not found" -ForegroundColor Red
    Start-Sleep 30
    Exit
}

#Set Required Environment Variables
if ($IsWindows) 
{
    $mainChainDataDir = "$env:APPDATA\StratisNode\strax\StraxMain"
    $sideChainDataDir = "$env:APPDATA\StratisNode\cirrus\CirrusMain"
    $cloneDir = "$HOME\Desktop\STRAX-SidechainMasternodes-InterFluxTest"
    $stratisMasternodeDashboardCloneDir = $cloneDir.Replace('SidechainMasternodes','StratisMasternodeDashboard')
}
    Else
    {
        Write-Host "ERROR: Windows OS was not detected." -ForegroundColor Red
        Start-Sleep 10
        Exit
    }

#Code Update
if ( -not ( Test-Path -Path $cloneDir\src\Stratis.StraxD -ErrorAction SilentlyContinue ) )
{ 
    Remove-Item -Path $cloneDir -Force -Recurse -ErrorAction SilentlyContinue
}

Write-Host (Get-TimeStamp) INFO: "Checking for updates.." -ForegroundColor Yellow
if ( -not ( Test-Path -Path $CloneDir -ErrorAction SilentlyContinue) ) 
{
    Write-Host (Get-TimeStamp) INFO: "Cloning SidechainMasternodes Branch" -ForegroundColor Cyan
    Start-Process git.exe -ArgumentList "clone --recurse-submodules $sidechainMasternodesRepo -b $sidechainMasternodeBranch $cloneDir" -Wait
}
    Else 
    {
        Set-Location $cloneDir
        Start-Process git.exe -ArgumentList "pull" -Wait
    }

if ( -not ( Test-Path -Path $stratisMasternodeDashboardCloneDir -ErrorAction SilentlyContinue ) )
{
    Write-Host (Get-TimeStamp) INFO:  "Cloning Stratis Masternode Dashboard" -ForegroundColor Cyan
    Start-Process git.exe -ArgumentList "clone $stratisMasternodeDashboardRepo $stratisMasternodeDashboardCloneDir" -Wait
}
    Else
    {
        Set-Location $stratisMasternodeDashboardCloneDir
        Start-Process git.exe -ArgumentList "pull" -Wait
    }

if ( Test-Path -Path $sideChainDataDir\blocks\_DBreezeSchema )
{
    Write-Host (Get-TimeStamp) ERROR:  "Your current data directory using an old data store. Resyncronisation is required..." -ForegroundColor Cyan
    $DeleteDataDir = Read-Host -Prompt "Enter 'Yes' to download an updated data directory or 'No' to exit the script"
    While ( $DeleteDataDir -ne "Yes" -and $DeleteDataDir -ne "No" )
    {
        ""
        $DeleteDataDir = Read-Host -Prompt "Enter 'Yes' to download an updated data directory or 'No' to exit the script"
        ""
    }
    Switch ( $DeleteDataDir )
    {
        Yes 
        {
            Rename-Item -Path $sideChainDataDir CirrusMain_OLD
            Write-Host (Get-TimeStamp) INFO:  "Downloading CirrusMain data directory..." -ForegroundColor Yellow
            Invoke-WebRequest -Uri http://academy.stratisplatform.com/CirrusMain-DataDir.zip -OutFile $env:TEMP\CirrusMain-DataDir.zip
            Expand-Archive -Path $env:TEMP\CirrusMain-DataDir.zip -DestinationPath $sideChainDataDir
        }
                
        No 
        { 
            Write-Host (Get-TimeStamp) "ERROR: You cannot run the node until you perform a clean IBD with this codebase or obtain the bootstrap data directory" -ForegroundColor DarkYellow
            Start-Sleep 30
            Exit
        }
    }
}

#Call Launch Script
Set-Location $cloneDir\Scripts\
& '.\LaunchSidechainMasternode.ps1'
# SIG # Begin signature block
# MIIO+wYJKoZIhvcNAQcCoIIO7DCCDugCAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUo/xfOiic54O3ekqU6WhLDRJQ
# T9KgggxDMIIFfzCCBGegAwIBAgIQB+RAO8y2U5CYymWFgvSvNDANBgkqhkiG9w0B
# AQsFADBsMQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMRkwFwYD
# VQQLExB3d3cuZGlnaWNlcnQuY29tMSswKQYDVQQDEyJEaWdpQ2VydCBFViBDb2Rl
# IFNpZ25pbmcgQ0EgKFNIQTIpMB4XDTE4MDcxNzAwMDAwMFoXDTIxMDcyMTEyMDAw
# MFowgZ0xEzARBgsrBgEEAYI3PAIBAxMCR0IxHTAbBgNVBA8MFFByaXZhdGUgT3Jn
# YW5pemF0aW9uMREwDwYDVQQFEwgxMDU1MDMzMzELMAkGA1UEBhMCR0IxDzANBgNV
# BAcTBkxvbmRvbjEaMBgGA1UEChMRU3RyYXRpcyBHcm91cCBMdGQxGjAYBgNVBAMT
# EVN0cmF0aXMgR3JvdXAgTHRkMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKC
# AQEAszr/7HowdxN95x+Utcge+d7wKwUA+kaIKrmlLFqFVg8ZdyvOZQN6gU/RRy6F
# NceRr5YAek4cg2T2MQs7REdkHDFzkAhOb1m/9b6fOJoF6YG3owhlyQZmtD0H64sj
# ZEpLkiuOFDOjxk8ICPrsoHcki5qoKdy7WkKdCWCTuSHKLNPUpfGyHYhjcdB5DTO2
# S4P+9JbIidPn0LR/NpjVCQXzFTTtteT+qj2cRTC4+ITIGWaWhulNiemLMSCF7Aar
# SAOQU8TSM9hLs4lwhtaLfx9j8bnQzz0YSYHUforeYrQCxmD66/KAup/OAZYQ6rYU
# kv5Eq+aNLd2Ot1FC1mQ3hOJW0QIDAQABo4IB6TCCAeUwHwYDVR0jBBgwFoAUj+h+
# 8G0yagAFI8dwl2o6kP9r6tQwHQYDVR0OBBYEFIV31zNBlgBtHnHANerRL6iOxCqa
# MCYGA1UdEQQfMB2gGwYIKwYBBQUHCAOgDzANDAtHQi0xMDU1MDMzMzAOBgNVHQ8B
# Af8EBAMCB4AwEwYDVR0lBAwwCgYIKwYBBQUHAwMwewYDVR0fBHQwcjA3oDWgM4Yx
# aHR0cDovL2NybDMuZGlnaWNlcnQuY29tL0VWQ29kZVNpZ25pbmdTSEEyLWcxLmNy
# bDA3oDWgM4YxaHR0cDovL2NybDQuZGlnaWNlcnQuY29tL0VWQ29kZVNpZ25pbmdT
# SEEyLWcxLmNybDBLBgNVHSAERDBCMDcGCWCGSAGG/WwDAjAqMCgGCCsGAQUFBwIB
# FhxodHRwczovL3d3dy5kaWdpY2VydC5jb20vQ1BTMAcGBWeBDAEDMH4GCCsGAQUF
# BwEBBHIwcDAkBggrBgEFBQcwAYYYaHR0cDovL29jc3AuZGlnaWNlcnQuY29tMEgG
# CCsGAQUFBzAChjxodHRwOi8vY2FjZXJ0cy5kaWdpY2VydC5jb20vRGlnaUNlcnRF
# VkNvZGVTaWduaW5nQ0EtU0hBMi5jcnQwDAYDVR0TAQH/BAIwADANBgkqhkiG9w0B
# AQsFAAOCAQEAadI2wIeKuOS2d3N49ilrMWEESf91zG6ifoJAES78+Q1X3pGWFltH
# h3J66FOwtC5XYg+UP3MDQybGb+yNBnABypIRE8RJhcmPeRbHjTqA2txl3B16evUm
# JX4Esmc7NOraGn03S9ZMH8Fa2coX/Epb/RbvY4e/z0O5dOsknfBOKXCEKrjzGVxt
# p9WIksRQLRdL0zqkKsxAU8gyU6O0neOCO4sYXvAb2CuLxJNMkEUO8mZe1Sz0DRLa
# hHueLB2EoKlyhFvA8SjehIcLQlE5FQvvxqmyy1yBovAWL6ktCaFCN6bLe/WTWPtu
# g5NNcn4cvq7X5gXQ8iNAPw+ZHmBipK0GXDCCBrwwggWkoAMCAQICEAPxtOFfOoLx
# FJZ4s9fYR1wwDQYJKoZIhvcNAQELBQAwbDELMAkGA1UEBhMCVVMxFTATBgNVBAoT
# DERpZ2lDZXJ0IEluYzEZMBcGA1UECxMQd3d3LmRpZ2ljZXJ0LmNvbTErMCkGA1UE
# AxMiRGlnaUNlcnQgSGlnaCBBc3N1cmFuY2UgRVYgUm9vdCBDQTAeFw0xMjA0MTgx
# MjAwMDBaFw0yNzA0MTgxMjAwMDBaMGwxCzAJBgNVBAYTAlVTMRUwEwYDVQQKEwxE
# aWdpQ2VydCBJbmMxGTAXBgNVBAsTEHd3dy5kaWdpY2VydC5jb20xKzApBgNVBAMT
# IkRpZ2lDZXJ0IEVWIENvZGUgU2lnbmluZyBDQSAoU0hBMikwggEiMA0GCSqGSIb3
# DQEBAQUAA4IBDwAwggEKAoIBAQCnU/oPsrUT8WTPhID8roA10bbXx6MsrBosrPGE
# rDo1EjqSkbpX5MTJ8y+oSDy31m7clyK6UXlhr0MvDbebtEkxrkRYPqShlqeHTyN+
# w2xlJJBVPqHKI3zFQunEemJFm33eY3TLnmMl+ISamq1FT659H8gTy3WbyeHhivgL
# DJj0yj7QRap6HqVYkzY0visuKzFYZrQyEJ+d8FKh7+g+03byQFrc+mo9G0utdrCM
# XO42uoPqMKhM3vELKlhBiK4AiasD0RaCICJ2615UOBJi4dJwJNvtH3DSZAmALeK2
# nc4f8rsh82zb2LMZe4pQn+/sNgpcmrdK0wigOXn93b89OgklAgMBAAGjggNYMIID
# VDASBgNVHRMBAf8ECDAGAQH/AgEAMA4GA1UdDwEB/wQEAwIBhjATBgNVHSUEDDAK
# BggrBgEFBQcDAzB/BggrBgEFBQcBAQRzMHEwJAYIKwYBBQUHMAGGGGh0dHA6Ly9v
# Y3NwLmRpZ2ljZXJ0LmNvbTBJBggrBgEFBQcwAoY9aHR0cDovL2NhY2VydHMuZGln
# aWNlcnQuY29tL0RpZ2lDZXJ0SGlnaEFzc3VyYW5jZUVWUm9vdENBLmNydDCBjwYD
# VR0fBIGHMIGEMECgPqA8hjpodHRwOi8vY3JsMy5kaWdpY2VydC5jb20vRGlnaUNl
# cnRIaWdoQXNzdXJhbmNlRVZSb290Q0EuY3JsMECgPqA8hjpodHRwOi8vY3JsNC5k
# aWdpY2VydC5jb20vRGlnaUNlcnRIaWdoQXNzdXJhbmNlRVZSb290Q0EuY3JsMIIB
# xAYDVR0gBIIBuzCCAbcwggGzBglghkgBhv1sAwIwggGkMDoGCCsGAQUFBwIBFi5o
# dHRwOi8vd3d3LmRpZ2ljZXJ0LmNvbS9zc2wtY3BzLXJlcG9zaXRvcnkuaHRtMIIB
# ZAYIKwYBBQUHAgIwggFWHoIBUgBBAG4AeQAgAHUAcwBlACAAbwBmACAAdABoAGkA
# cwAgAEMAZQByAHQAaQBmAGkAYwBhAHQAZQAgAGMAbwBuAHMAdABpAHQAdQB0AGUA
# cwAgAGEAYwBjAGUAcAB0AGEAbgBjAGUAIABvAGYAIAB0AGgAZQAgAEQAaQBnAGkA
# QwBlAHIAdAAgAEMAUAAvAEMAUABTACAAYQBuAGQAIAB0AGgAZQAgAFIAZQBsAHkA
# aQBuAGcAIABQAGEAcgB0AHkAIABBAGcAcgBlAGUAbQBlAG4AdAAgAHcAaABpAGMA
# aAAgAGwAaQBtAGkAdAAgAGwAaQBhAGIAaQBsAGkAdAB5ACAAYQBuAGQAIABhAHIA
# ZQAgAGkAbgBjAG8AcgBwAG8AcgBhAHQAZQBkACAAaABlAHIAZQBpAG4AIABiAHkA
# IAByAGUAZgBlAHIAZQBuAGMAZQAuMB0GA1UdDgQWBBSP6H7wbTJqAAUjx3CXajqQ
# /2vq1DAfBgNVHSMEGDAWgBSxPsNpA/i/RwHUmCYaCALvY2QrwzANBgkqhkiG9w0B
# AQsFAAOCAQEAGTNKDIEzN9utNsnkyTq7tRsueqLi9ENCF56/TqFN4bHb6YHdnwHy
# 5IjV6f4J/SHB7F2A0vDWwUPC/ncr2/nXkTPObNWyGTvmLtbJk0+IQI7N4fV+8Q/G
# WVZy6OtqQb0c1UbVfEnKZjgVwb/gkXB3h9zJjTHJDCmiM+2N4ofNiY0/G//V4BqX
# i3zabfuoxrI6Zmt7AbPN2KY07BIBq5VYpcRTV6hg5ucCEqC5I2SiTbt8gSVkIb7P
# 7kIYQ5e7pTcGr03/JqVNYUvsRkG4Zc64eZ4IlguBjIo7j8eZjKMqbphtXmHGlreK
# uWEtk7jrDgRD1/X+pvBi1JlqpcHB8GSUgDGCAiIwggIeAgEBMIGAMGwxCzAJBgNV
# BAYTAlVTMRUwEwYDVQQKEwxEaWdpQ2VydCBJbmMxGTAXBgNVBAsTEHd3dy5kaWdp
# Y2VydC5jb20xKzApBgNVBAMTIkRpZ2lDZXJ0IEVWIENvZGUgU2lnbmluZyBDQSAo
# U0hBMikCEAfkQDvMtlOQmMplhYL0rzQwCQYFKw4DAhoFAKB4MBgGCisGAQQBgjcC
# AQwxCjAIoAKAAKECgAAwGQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwHAYKKwYB
# BAGCNwIBCzEOMAwGCisGAQQBgjcCARUwIwYJKoZIhvcNAQkEMRYEFGGCCye77Gwh
# Dgqj6ecZUFnqRCdDMA0GCSqGSIb3DQEBAQUABIIBAELjVMIgVKQaYn3kY46j20Gg
# +vK5L/IZS+i10utfdYO39x3jIcSGn0uJAzi/MM8Lmhg4oTToIJSxRIUWfv/5O3hA
# sqVoLhRwe5kbZ2gw82O2y+ADYI9INsu8Z0uEO0PCgIlxgdeTvky2EtA7VB5kHGZf
# jUwY2DWcN6FJvIMYpQ/9K8R0qVzX6g2sHozCzpxnDFs+wmfeoVy/Yhkq5CgZMthH
# yxhVW5cDzMMwYDVyFO6w6YH7rJMWMzmwrELFgqRMuXvixBe/JEq35OpjszPDmOS2
# LCiMA13cyrisVgFWGr22ZGng+PGObwy0KBsZR5Tn2PacyMM0ndhxJ5jeNxKYezU=
# SIG # End signature block
