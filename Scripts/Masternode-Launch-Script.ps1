Write-Host STRAX Masternode Launch Script - Version 2 -ForegroundColor Cyan
""
""
Start-Sleep 5

Clear-Host
$mainChainAPIPort = '17103'
$sideChainAPIPort = '37223'
$sidechainMasternodesRepo = "https://github.com/stratisproject/StratisFullNode.git"
$sidechainMasternodeBranch = "SidechainMasternode"
$stratisMasternodeDashboardRepo = "https://github.com/stratisproject/StratisMasternodeDashboard"

#Create Functions
function Get-TimeStamp 
{
    return "[{0:dd/MM/yy} {0:HH:mm:ss}]" -f (Get-Date)
}

#Check for pre-requisites
$dotnetVersion = dotnet --list-sdks
if ( -not ($dotnetVersion -like "6.0.*") ) 
{
    Write-Host "ERROR:  .NET Core 6.0 SDK or above not found, please download and install .NET Core SDK 6.0" -ForegroundColor Red
    Start-Sleep 30
    Exit
}

$gitVersion = git --version
if ( -not ($gitVersion -ne $null) ) 
{
    Write-Host "ERROR:  git not found, please download and install git" -ForegroundColor Red
    Start-Sleep 30
    Exit
}

$pwshVersion = $PSVersionTable.PSVersion.ToString()
if ( -not ($pwshVersion -gt "7.1") ) 
{
    Write-Host "ERROR:  PowerShell Core not found, please launch this script using 'PWSH' or download and install PowerShell 7 or greater" -ForegroundColor Red
    Start-Sleep 30
    Exit
}   

#Set Required Environment Variables
if ($IsWindows) 
{
    $mainChainDataDir = "$env:APPDATA\StratisNode\strax\StraxMain"
    $sideChainDataDir = "$env:APPDATA\StratisNode\cirrus\CirrusMain"
    $cloneDir = "$HOME\Desktop\STRAX-SidechainMasternodes"
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

#Call Launch Script
Set-Location $cloneDir\Scripts\
& '.\LaunchSidechainMasternode.ps1'

# SIG # Begin signature block
# MIIO+gYJKoZIhvcNAQcCoIIO6zCCDucCAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUcq/jwj/6Awtj69NhzCSzoIm6
# 2VigggxCMIIFfjCCBGagAwIBAgIQCrk836uc/wPyOiuycqPb5zANBgkqhkiG9w0B
# AQsFADBsMQswCQYDVQQGEwJVUzEVMBMGA1UEChMMRGlnaUNlcnQgSW5jMRkwFwYD
# VQQLExB3d3cuZGlnaWNlcnQuY29tMSswKQYDVQQDEyJEaWdpQ2VydCBFViBDb2Rl
# IFNpZ25pbmcgQ0EgKFNIQTIpMB4XDTIxMDQyMjAwMDAwMFoXDTI0MDcxOTIzNTk1
# OVowgZ0xHTAbBgNVBA8MFFByaXZhdGUgT3JnYW5pemF0aW9uMRMwEQYLKwYBBAGC
# NzwCAQMTAkdCMREwDwYDVQQFEwgxMDU1MDMzMzELMAkGA1UEBhMCR0IxDzANBgNV
# BAcTBkxvbmRvbjEaMBgGA1UEChMRU3RyYXRpcyBHcm91cCBMdGQxGjAYBgNVBAMT
# EVN0cmF0aXMgR3JvdXAgTHRkMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKC
# AQEAkn7b1/xIuE2TqJe+loS4l6g7UOKBpivRPKt1wIoaSj0sc1cbnlFSzDOcAnCS
# WVsHtX99Yk4mW9cCiWXuP0EcURF5pgu9TWiALZRVXefD0w3Luio/Ej4uQ741+Tf6
# hCrgYn9Ui/a8VZB+dOF2I3Ixq0Y+dZQz61Ovp7FfRviBJBkN2cCY1YJEcAr1Um3Y
# EmxpKEAb3OfY9AXZCT22mCnvMwpPK80mY6e1T/928wrwHfEU+0IVl/blhEYvxtNf
# wgZVVHQ4wvmomW20iA+KyOc3EXbhJhOCP+4hrF6A6eUcrxyJd0wRFJkBd7B6LzKZ
# OyIfjIaHmCDZIaCjbolyOLVl8wIDAQABo4IB6DCCAeQwHwYDVR0jBBgwFoAUj+h+
# 8G0yagAFI8dwl2o6kP9r6tQwHQYDVR0OBBYEFK/Xc5t+2Ql1Dlo2AuMKBW1RIL71
# MCYGA1UdEQQfMB2gGwYIKwYBBQUHCAOgDzANDAtHQi0xMDU1MDMzMzAOBgNVHQ8B
# Af8EBAMCB4AwEwYDVR0lBAwwCgYIKwYBBQUHAwMwewYDVR0fBHQwcjA3oDWgM4Yx
# aHR0cDovL2NybDMuZGlnaWNlcnQuY29tL0VWQ29kZVNpZ25pbmdTSEEyLWcxLmNy
# bDA3oDWgM4YxaHR0cDovL2NybDQuZGlnaWNlcnQuY29tL0VWQ29kZVNpZ25pbmdT
# SEEyLWcxLmNybDBKBgNVHSAEQzBBMDYGCWCGSAGG/WwDAjApMCcGCCsGAQUFBwIB
# FhtodHRwOi8vd3d3LmRpZ2ljZXJ0LmNvbS9DUFMwBwYFZ4EMAQMwfgYIKwYBBQUH
# AQEEcjBwMCQGCCsGAQUFBzABhhhodHRwOi8vb2NzcC5kaWdpY2VydC5jb20wSAYI
# KwYBBQUHMAKGPGh0dHA6Ly9jYWNlcnRzLmRpZ2ljZXJ0LmNvbS9EaWdpQ2VydEVW
# Q29kZVNpZ25pbmdDQS1TSEEyLmNydDAMBgNVHRMBAf8EAjAAMA0GCSqGSIb3DQEB
# CwUAA4IBAQA6C/3OlxNLLPbXdaD6o5toGRufe+oRFPgCxkLuhHW10VM2tGJRLMgq
# dHwE8TRnefYibr+TyYzWd/XNN6DEks8T73GUNZxkwhWpAqMBiWDiSvPe3OcnX5J2
# V6OKsynobw/F+hivNAOa98HhRPuh4dZp/Xswl+mZY+eKSLJ539pHpeelKobnYNIu
# PFh1iYbk5+80JzqppOSrKagZ0ahHriJRJrqPkTjv+oRmp2o5vEYlAiEhQoyfKXLN
# zNf99EU1qtsH5vVehUgluP9oHBABMYU+bAKXYeULWFRrSSkFowpp7mfAAbKZX4Hf
# QwCNt6Wh8JhqOdXuudIwNiAKUC6NokxRMIIGvDCCBaSgAwIBAgIQA/G04V86gvEU
# lniz19hHXDANBgkqhkiG9w0BAQsFADBsMQswCQYDVQQGEwJVUzEVMBMGA1UEChMM
# RGlnaUNlcnQgSW5jMRkwFwYDVQQLExB3d3cuZGlnaWNlcnQuY29tMSswKQYDVQQD
# EyJEaWdpQ2VydCBIaWdoIEFzc3VyYW5jZSBFViBSb290IENBMB4XDTEyMDQxODEy
# MDAwMFoXDTI3MDQxODEyMDAwMFowbDELMAkGA1UEBhMCVVMxFTATBgNVBAoTDERp
# Z2lDZXJ0IEluYzEZMBcGA1UECxMQd3d3LmRpZ2ljZXJ0LmNvbTErMCkGA1UEAxMi
# RGlnaUNlcnQgRVYgQ29kZSBTaWduaW5nIENBIChTSEEyKTCCASIwDQYJKoZIhvcN
# AQEBBQADggEPADCCAQoCggEBAKdT+g+ytRPxZM+EgPyugDXRttfHoyysGiys8YSs
# OjUSOpKRulfkxMnzL6hIPLfWbtyXIrpReWGvQy8Nt5u0STGuRFg+pKGWp4dPI37D
# bGUkkFU+ocojfMVC6cR6YkWbfd5jdMueYyX4hJqarUVPrn0fyBPLdZvJ4eGK+AsM
# mPTKPtBFqnoepViTNjS+Ky4rMVhmtDIQn53wUqHv6D7TdvJAWtz6aj0bS612sIxc
# 7ja6g+owqEze8QsqWEGIrgCJqwPRFoIgInbrXlQ4EmLh0nAk2+0fcNJkCYAt4rad
# zh/yuyHzbNvYsxl7ilCf7+w2Clyat0rTCKA5ef3dvz06CSUCAwEAAaOCA1gwggNU
# MBIGA1UdEwEB/wQIMAYBAf8CAQAwDgYDVR0PAQH/BAQDAgGGMBMGA1UdJQQMMAoG
# CCsGAQUFBwMDMH8GCCsGAQUFBwEBBHMwcTAkBggrBgEFBQcwAYYYaHR0cDovL29j
# c3AuZGlnaWNlcnQuY29tMEkGCCsGAQUFBzAChj1odHRwOi8vY2FjZXJ0cy5kaWdp
# Y2VydC5jb20vRGlnaUNlcnRIaWdoQXNzdXJhbmNlRVZSb290Q0EuY3J0MIGPBgNV
# HR8EgYcwgYQwQKA+oDyGOmh0dHA6Ly9jcmwzLmRpZ2ljZXJ0LmNvbS9EaWdpQ2Vy
# dEhpZ2hBc3N1cmFuY2VFVlJvb3RDQS5jcmwwQKA+oDyGOmh0dHA6Ly9jcmw0LmRp
# Z2ljZXJ0LmNvbS9EaWdpQ2VydEhpZ2hBc3N1cmFuY2VFVlJvb3RDQS5jcmwwggHE
# BgNVHSAEggG7MIIBtzCCAbMGCWCGSAGG/WwDAjCCAaQwOgYIKwYBBQUHAgEWLmh0
# dHA6Ly93d3cuZGlnaWNlcnQuY29tL3NzbC1jcHMtcmVwb3NpdG9yeS5odG0wggFk
# BggrBgEFBQcCAjCCAVYeggFSAEEAbgB5ACAAdQBzAGUAIABvAGYAIAB0AGgAaQBz
# ACAAQwBlAHIAdABpAGYAaQBjAGEAdABlACAAYwBvAG4AcwB0AGkAdAB1AHQAZQBz
# ACAAYQBjAGMAZQBwAHQAYQBuAGMAZQAgAG8AZgAgAHQAaABlACAARABpAGcAaQBD
# AGUAcgB0ACAAQwBQAC8AQwBQAFMAIABhAG4AZAAgAHQAaABlACAAUgBlAGwAeQBp
# AG4AZwAgAFAAYQByAHQAeQAgAEEAZwByAGUAZQBtAGUAbgB0ACAAdwBoAGkAYwBo
# ACAAbABpAG0AaQB0ACAAbABpAGEAYgBpAGwAaQB0AHkAIABhAG4AZAAgAGEAcgBl
# ACAAaQBuAGMAbwByAHAAbwByAGEAdABlAGQAIABoAGUAcgBlAGkAbgAgAGIAeQAg
# AHIAZQBmAGUAcgBlAG4AYwBlAC4wHQYDVR0OBBYEFI/ofvBtMmoABSPHcJdqOpD/
# a+rUMB8GA1UdIwQYMBaAFLE+w2kD+L9HAdSYJhoIAu9jZCvDMA0GCSqGSIb3DQEB
# CwUAA4IBAQAZM0oMgTM32602yeTJOru1Gy56ouL0Q0IXnr9OoU3hsdvpgd2fAfLk
# iNXp/gn9IcHsXYDS8NbBQ8L+dyvb+deRM85s1bIZO+Yu1smTT4hAjs3h9X7xD8ZZ
# VnLo62pBvRzVRtV8ScpmOBXBv+CRcHeH3MmNMckMKaIz7Y3ih82JjT8b/9XgGpeL
# fNpt+6jGsjpma3sBs83YpjTsEgGrlVilxFNXqGDm5wISoLkjZKJNu3yBJWQhvs/u
# QhhDl7ulNwavTf8mpU1hS+xGQbhlzrh5ngiWC4GMijuPx5mMoypumG1eYcaWt4q5
# YS2TuOsOBEPX9f6m8GLUmWqlwcHwZJSAMYICIjCCAh4CAQEwgYAwbDELMAkGA1UE
# BhMCVVMxFTATBgNVBAoTDERpZ2lDZXJ0IEluYzEZMBcGA1UECxMQd3d3LmRpZ2lj
# ZXJ0LmNvbTErMCkGA1UEAxMiRGlnaUNlcnQgRVYgQ29kZSBTaWduaW5nIENBIChT
# SEEyKQIQCrk836uc/wPyOiuycqPb5zAJBgUrDgMCGgUAoHgwGAYKKwYBBAGCNwIB
# DDEKMAigAoAAoQKAADAZBgkqhkiG9w0BCQMxDAYKKwYBBAGCNwIBBDAcBgorBgEE
# AYI3AgELMQ4wDAYKKwYBBAGCNwIBFTAjBgkqhkiG9w0BCQQxFgQUPetli19yk6kZ
# pFFOeCjk1XuwAx4wDQYJKoZIhvcNAQEBBQAEggEAGZs4PMx8MRyIgZjQhYZ4FuVS
# +XAQx9PsLWezcsrHJMSubHCcbfzPRcuVqYmDPZV4IpqJc8iGtl8yvhWmJNfr3jhw
# lOrqgA2n4yxJTjINWNLitHmUzkQ/dHDIz0j2CF4qux9yM5uwqD6uVH/6G2utCN0g
# LwnzW/39kNZBS4GxGBk+RgsNveTprj4IwglUZqaDzy0EZ68St07cZHLCnwkJuytv
# 6/im00MU510CCpMagUZDIOllxYHTxNFsIOBxcHiVyLth+J1dvFwNcjqgfVG6DQpS
# IYDte17+GdoN7jCK0Q6QrgBsSqlbcyYq3nk4FEpsa2BA7wzosDnhZVOXqd7DxQ==
# SIG # End signature block
