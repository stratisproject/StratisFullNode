#Create Functions
function Get-IndexerStatus
{
    $Headers = @{}
    $Headers.Add("Accept","application/json")
    $AsyncLoopStats = Invoke-WebRequest -Uri http://localhost:$API/api/Dashboard/AsyncLoopsStats | Select-Object -ExpandProperty content 
    if ( $AsyncLoopStats.Contains("Fault Reason: Missing outpoint data") )
    {
        Write-Host "ERROR: Indexing Database is corrupt" -ForegroundColor Red
        Write-Host "Would you like to delete the database?"
        $DeleteDB = Read-Host -Prompt "Enter 'Yes' to remove the Indexing Database or 'No' to exit the script"
        While ( $DeleteDB -ne "Yes" -and $DeleteDB -ne "No" )
        {
            $DeleteDB = Read-Host -Prompt "Enter 'Yes' to remove the indexing database or 'No' to exit the script"
        }
        Switch ( $DeleteDB )
        {
            Yes 
            {
                Shutdown-MainchainNode
                Remove-Item -Path $API\addressindex.litedb -Force
                if ( -not ( Get-Item -Path $API\addressindex.litedb ) )
                {
                    Write-Host "SUCCESS: Indexing Database has been removed. Please re-run the script" -ForegroundColor Green
                    Start-Sleep 10
                    Exit
                }
                    Else
                    {
                        Write-Host "ERROR: Something went wrong. Please contact support in Discord" -ForegroundColor Red
                    }
            }
                
            No 
            { 
                Shutdown-MainchainNode
                Write-Host "WARNING: Masternode cannot run until Indexing Database is recovered. This will require a re-index. Please remove the addressindex.litedb file and re-run the script" -ForegroundColor DarkYellow
                Start-Sleep 10
                Exit
            }
        }
    }
}

function Shutdown-MainchainNode
{
    Write-Host "Shutting down Mainchain Node..." -ForegroundColor Yellow
    $Headers = @{}
    $Headers.Add("Accept","application/json")
    Invoke-WebRequest -Uri http://localhost:$mainChainAPIPort/api/Node/shutdown -Method Post -ContentType application/json-patch+json -Headers $Headers -Body "true" -ErrorAction SilentlyContinue | Out-Null

    While ( Test-Connection -TargetName 127.0.0.1 -TCPPort $mainChainAPIPort -ErrorAction SilentlyContinue )
    {
        Write-Host "Waiting for node to stop..." -ForegroundColor Yellow
        Start-Sleep 5
    }

    Write-Host "SUCCESS: Mainchain Node shutdown" -ForegroundColor Green
    Write-Host ""
}

function Shutdown-SidechainNode
{
    Write-Host "Shutting down Sidechain Node..." -ForegroundColor Yellow
    $Headers = @{}
    $Headers.Add("Accept","application/json")
    Invoke-WebRequest -Uri http://localhost:$sideChainAPIPort/api/Node/shutdown -Method Post -ContentType application/json-patch+json -Headers $Headers -Body "true" -ErrorAction SilentlyContinue | Out-Null

    While ( Test-Connection -TargetName 127.0.0.1 -TCPPort $sideChainAPIPort -ErrorAction SilentlyContinue )
    {
        Write-Host "Waiting for node to stop..." -ForegroundColor Yellow
        Start-Sleep 5
    }

     Write-Host "SUCCESS: Sidechain Node shutdown" -ForegroundColor Green
     Write-Host ""
}

function Get-MaxHeight 
{
    $Height = @{}
    $Peers = Invoke-WebRequest -Uri http://localhost:$API/api/ConnectionManager/getpeerinfo | ConvertFrom-Json
    foreach ( $Peer in $Peers )
    {
        if ( $Peer.subver -eq "StratisNode:0.13.0 (70000)" )
        {
        }
            Else
            {
                $Height.Add($Peer.id,$Peer.startingheight)
            }
    }    
    
    $MaxHeight = $Height.Values | Measure-Object -Maximum | Select-Object -ExpandProperty Maximum
    $MaxHeight
}       

function Get-LocalHeight 
{
    $StatsRequest = Invoke-WebRequest -Uri http://localhost:$API/api/Node/status
    $Stats = ConvertFrom-Json $StatsRequest
    $LocalHeight = $Stats.blockStoreHeight
    $LocalHeight
}

function Get-WalletHeight
{
    $WalletHeight = Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/general-info?Name=$Wallet | ConvertFrom-Json | Select-Object -ExpandProperty lastBlockSyncedHeight
    $WalletHeight
}

function Get-LocalIndexerHeight 
{
    $IndexStatsRequest = Invoke-WebRequest -Uri http://localhost:$API/api/BlockStore/addressindexertip
    $IndexStats = ConvertFrom-Json $IndexStatsRequest
    $LocalIndexHeight = $IndexStats.tipHeight
    $LocalIndexHeight
}

function Get-BlockStoreStatus
{
    $FeatureStatus = Invoke-WebRequest -Uri http://localhost:$API/api/Node/status | ConvertFrom-Json | Select-Object -ExpandProperty featuresData
    $BlockStoreStatus = $FeatureStatus | Where-Object { $_.namespace -eq "Stratis.Bitcoin.Features.BlockStore.BlockStoreFeature" }
    $BlockStoreStatus.state
}

#Check for an existing running node
Write-Host (Get-TimeStamp) "Checking for running Mainchain Node" -ForegroundColor Cyan
if ( Test-Connection -TargetName 127.0.0.1 -TCPPort $mainChainAPIPort )
{
    Write-Host (Get-TimeStamp) "WARNING: A node is already running, will perform a graceful shutdown" -ForegroundColor DarkYellow
    ""
    Shutdown-MainchainNode
}

Write-Host (Get-TimeStamp) "Checking for running Sidechain Node" -ForegroundColor Cyan
if ( Test-Connection -TargetName 127.0.0.1 -TCPPort $sideChainAPIPort )
{
    Write-Host (Get-TimeStamp) "WARNING: A node is already running, will perform a graceful shutdown" -ForegroundColor DarkYellow
    ""
    Shutdown-SidechainNode
}

Set-Location $cloneDir\src\Stratis.CirrusMinerD

#Start Mainchain Node
$API = $mainChainAPIPort
Write-Host (Get-TimeStamp) "Starting Mainchain Masternode" -ForegroundColor Cyan
$StartNode = Start-Process dotnet -ArgumentList "run -c Release -- -mainchain -testnet -addressindex=1 -apiport=$mainChainAPIPort" -PassThru

#Wait for API
While ( -not ( Test-Connection -TargetName 127.0.0.1 -TCPPort $API ) ) 
{
    Write-Host (Get-TimeStamp) "Waiting for API..." -ForegroundColor Yellow  
    Start-Sleep 3
    if ( $StartNode.HasExited -eq $true )
    {
        Write-Host (Get-TimeStamp) "ERROR: Something went wrong. Please contact support in Discord" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }
}

#Wait for BlockStore Feature
While ( ( Get-BlockStoreStatus ) -ne "Initialized" )  
{ 
    Write-Host (Get-TimeStamp) "Waiting for BlockStore to Initialize..." -ForegroundColor Yellow
    Start-Sleep 10
    if ( $StartNode.HasExited -eq $true )
    {
        Write-Host (Get-TimeStamp) "ERROR: Something went wrong. Please contact support in Discord" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }
}

#Wait for IBD
While ( ( Get-MaxHeight ) -eq $null ) 
{
    Write-Host (Get-TimeStamp) "Waiting for Peers..." -ForegroundColor Yellow
    Start-Sleep 10
}

While ( ( Get-MaxHeight ) -gt ( Get-LocalIndexerHeight ) ) 
{
    $a = Get-MaxHeight
    $b = Get-LocalIndexerHeight 
    $c = $a - $b
    ""
    Write-Host (Get-TimeStamp) "The Indexed Height is $b" -ForegroundColor Yellow
    Write-Host (Get-TimeStamp) "The Current Tip is $a" -ForegroundColor Yellow
    Write-Host (Get-TimeStamp) "$c Blocks Require Indexing..." -ForegroundColor Yellow
    Start-Sleep 10
    Get-IndexerStatus
}

#Clear Variables
if ( Get-Variable a -ErrorAction SilentlyContinue ) { Clear-Variable a }
if ( Get-Variable b -ErrorAction SilentlyContinue ) { Clear-Variable b }
if ( Get-Variable c -ErrorAction SilentlyContinue ) { Clear-Variable c }

#Start Sidechain Node
$API = $sideChainAPIPort
Write-Host (Get-TimeStamp) "Starting Sidechain Masternode" -ForegroundColor Cyan
$StartNode = Start-Process dotnet -ArgumentList "run -c Release -- -sidechain -testnet -apiport=$sideChainAPIPort -counterchainapiport=$mainChainAPIPort" -PassThru

#Wait for API
While ( -not ( Test-Connection -TargetName 127.0.0.1 -TCPPort $API ) ) 
{
    Write-Host (Get-TimeStamp) "Waiting for API..." -ForegroundColor Yellow  
    Start-Sleep 3
    if ( $StartNode.HasExited -eq $true )
    {
        Write-Host (Get-TimeStamp) "ERROR: Something went wrong. Please contact support in Discord" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }
}

#Wait for BlockStore Feature
While ( ( Get-BlockStoreStatus ) -ne "Initialized" )  
{ 
    Write-Host (Get-TimeStamp) "Waiting for BlockStore to Initialize..." -ForegroundColor Yellow
    Start-Sleep 10
    if ( $StartNode.HasExited -eq $true )
    {
        Write-Host (Get-TimeStamp) "ERROR: Something went wrong. Please contact support in Discord" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }
}

#Wait for IBD
While ( ( Get-MaxHeight ) -eq $null ) 
{
Write-Host (Get-TimeStamp) "Waiting for Peers..." -ForegroundColor Yellow
Start-Sleep 10
}

While ( ( Get-MaxHeight ) -gt ( Get-LocalHeight ) ) 
{
    $a = Get-MaxHeight
    $b = Get-LocalHeight 
    $c = $a - $b
    ""
    Write-Host (Get-TimeStamp) "The Local Synced Height is $b" -ForegroundColor Yellow
    Write-Host (Get-TimeStamp) "The Current Tip is $a" -ForegroundColor Yellow
    Write-Host (Get-TimeStamp) "$c Blocks are Required..." -ForegroundColor Yellow
    Start-Sleep 10
}

""
Write-Host (Get-TimeStamp) "SUCCESS: STRAX Blockchain and Cirrus Blockchain are now fully synchronised!" -ForegroundColor Green
""
#Check Collateral Wallet Existence
$API = $mainChainAPIPort
Write-Host (Get-TimeStamp) INFO: "Assessing Cirrus Masternode Requirements" -ForegroundColor Cyan
""
$CollateralWallet = Read-Host "Please Enter the Name of the STRAX Wallet that contains the required Collateral"
$Wallet = $CollateralWallet
""
$LoadedWallets = Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/list-wallets -UseBasicParsing | Select-Object -ExpandProperty content | ConvertFrom-Json | Select-Object -ExpandProperty walletNames

if ( $LoadedWallets -contains $Wallet )
{
    Write-Host (Get-TimeStamp) "SUCCESS: Collateral wallet found!" -ForegroundColor Green
}
    Else
    {
        Write-Host (Get-TimeStamp) "ERROR: No Wallets could be found.. Please restore a wallet that holds the required collateral" -ForegroundColor Red
        ""
        $RestoreWallet = Read-Host -Prompt 'Would you like to restore the wallet using this script? Enter "Yes" to continue or "No" to exit the script'
        ""
        While ( $RestoreWallet -ne "Yes" -and $RestoreWallet -ne "No" )
        {
            ""
            $RestoreWallet = Read-Host -Prompt "Enter 'Yes' to continue or 'No' to exit the script"
            ""
        }
        Switch ( $RestoreWallet )
        {
            Yes
            {
                $WalletMnemonic = Read-Host "Please enter your 12-Words used to recover your wallet"
                Clear-Host
                $WalletPassphrase = Read-Host "Please enter your Wallet Passphrase"
                Clear-Host
                $WalletPassword = Read-Host "Please enter a password used to encrypt the wallet"
                Clear-Host
                if ( -not ( $Wallet ) ) { $ErrorVar = 1 }
                if ( -not ( $WalletMnemonic ) ) { $ErrorVar = 1 }
                if ( -not ( $WalletPassword ) ) { $ErrorVar = 1 }
                if ( $ErrorVar ) 
                {
                    Write-Host (Get-TimeStamp) "ERROR: There was some missing wallet detail - Please re-run this script" -ForegroundColor Red
                    Start-Sleep 30
                    Exit
                }
            
                $Body = @{}
                $Body.Add("mnemonic",$WalletMnemonic)
                if ( $WalletPassphrase )
                {
                    $Body.Add("passphrase",$WalletPassphrase)
                }
                    Else
                    {
                        $Body.Add("passphrase","")
                    }
                $Body.Add("password",$WalletPassword)
                $Body.Add("name",$Wallet)
                $Body.Add("creationDate","2020-11-01T00:00:01.690Z")
                $Body = $Body | ConvertTo-Json
                $RestoreWallet = Invoke-WebRequest -Uri http://localhost:$API/api/wallet/recover -UseBasicParsing -Method Post -Body $Body -ContentType "application/json"
                if ( (Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/list-wallets -UseBasicParsing | Select-Object -ExpandProperty content | ConvertFrom-Json | Select-Object -ExpandProperty walletNames) -notcontains $Wallet ) 
                {                    
                    Write-Host (Get-TimeStamp) "ERROR: There was some missing wallet detail - Please re-run this script" -ForegroundColor Red
                    Start-Sleep 30
                    Exit
                }
                Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/remove-transactions?WalletName=$Wallet"&"all=true"&"ReSync=true -UseBasicParsing -Method Delete
                Clear-Host
                Write-Host (Get-TimeStamp) INFO: "Syncing $Wallet - This may take some time and -1 may be dispalyed for some time. The process is wholly dependant on avaialble resource, please do no close this window..." -ForegroundColor Cyan
                While ( (Get-WalletHeight) -ne (Get-LocalHeight) )
                {
                    $a = Get-LocalHeight
                    $b = Get-WalletHeight 
                    $c = $a - $b
                    ""
                    Write-Host (Get-TimeStamp) "The Wallet Synced Height is $b" -ForegroundColor Yellow
                    Write-Host (Get-TimeStamp) "The Current Tip is $a" -ForegroundColor Yellow
                    Write-Host (Get-TimeStamp) "$c Blocks are Required..." -ForegroundColor Yellow
                    Start-Sleep 10
                }
            }

            No
            {
                Write-Host (Get-TimeStamp) "ERROR: There was some missing wallet detail - Please re-run this script" -ForegroundColor Red
                Start-Sleep 30
                Exit
            }
        }
    }
    

#Check Wallet Balance
$CollateralWalletBalance = (Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/balance?WalletName=$Wallet -Method Get | Select-Object -ExpandProperty content | ConvertFrom-Json | Select-Object -ExpandProperty balances | Select-Object -ExpandProperty spendableamount) / 100000000
if ( $CollateralWalletBalance -ge 100000 )
{
    Write-Host (Get-TimeStamp) "SUCCESS: Collateral Wallet contains a balance of over 100,000 STRAX!" -ForegroundColor Green
}
    Else
    {
        Write-Host (Get-TimeStamp) "ERROR: Collateral Wallet does not contain a balance of over 100,001 STRAX! Please run again and define a wallet that contains the collateral amount..." -ForegroundColor Red
        Start-Sleep 30
        Exit
    }
    

#Check Cirrus Wallet Existence
if ( $WalletPassphrase ) { Clear-Variable WalletPassphrase }
$API = $sideChainAPIPort
""
$CirrusWallet = Read-Host "Please Enter the Name of the Cirrus Wallet that contains the required balance of 501 CRS to fund the registration fee"
$Wallet = $CirrusWallet
""
$LoadedWallets = Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/list-wallets -UseBasicParsing | Select-Object -ExpandProperty content | ConvertFrom-Json | Select-Object -ExpandProperty walletNames

if ( $LoadedWallets -contains $Wallet )
{
    Write-Host (Get-TimeStamp) "SUCCESS: $Wallet found!" -ForegroundColor Green
}
    Else
    {
        Write-Host (Get-TimeStamp) "ERROR: No wallet named $Wallet could be found.. Please restore a wallet that holds the required collateral" -ForegroundColor Red
        ""
        $RestoreWallet = Read-Host -Prompt 'Would you like to restore the wallet using this script? Enter "Yes" to continue or "No" to exit the script'
        ""
        While ( $RestoreWallet -ne "Yes" -and $RestoreWallet -ne "No" )
        {
            ""
            $RestoreWallet = Read-Host -Prompt "Enter 'Yes' to continue or 'No' to exit the script"
            ""
        }
        Switch ( $RestoreWallet )
        {
            Yes
            {
                $WalletMnemonic = Read-Host "Please enter your 12-Words used to recover your wallet"
                Clear-Host
                $WalletPassphrase = Read-Host "Please enter your Wallet Passphrase"
                Clear-Host
                $WalletPassword = Read-Host "Please enter a password used to encrypt the wallet"
                Clear-Host
                if ( -not ( $CirrusWallet ) ) { $ErrorVar = 1 }
                if ( -not ( $WalletMnemonic ) ) { $ErrorVar = 1 }
                if ( -not ( $WalletPassword ) ) { $ErrorVar = 1 }
                if ( $ErrorVar ) 
                {
                    Write-Host (Get-TimeStamp) "ERROR: There was some missing wallet detail - Please re-run this script" -ForegroundColor Red
                    Start-Sleep 30
                    Exit
                }
            
                $Body = @{}
                $Body.Add("mnemonic",$WalletMnemonic)
                if ( $WalletPassphrase )
                {
                    $Body.Add("passphrase",$WalletPassphrase)
                }
                    Else
                    {
                        $Body.Add("passphrase","")
                    }
                $Body.Add("password",$WalletPassword)
                $Body.Add("name",$Wallet)
                $Body.Add("creationDate","2020-11-01T00:00:01.690Z")
                $Body = $Body | ConvertTo-Json
                $RestoreWallet = Invoke-WebRequest -Uri http://localhost:$API/api/wallet/recover -UseBasicParsing -Method Post -Body $Body -ContentType "application/json"
                if ( (Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/list-wallets -UseBasicParsing | Select-Object -ExpandProperty content | ConvertFrom-Json | Select-Object -ExpandProperty walletNames) -notcontains $Wallet ) 
                {                    
                    Write-Host (Get-TimeStamp) "ERROR: There was some missing wallet detail - Please re-run this script" -ForegroundColor Red
                    Start-Sleep 30
                    Exit
                }
                Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/remove-transactions?WalletName=$Wallet"&"all=true"&"ReSync=true -UseBasicParsing -Method Delete

                While ( (Get-WalletHeight) -ne (Get-LocalHeight) )
                {
                    $a = Get-LocalHeight
                    $b = Get-WalletHeight 
                    $c = $a - $b
                    ""
                    Write-Host (Get-TimeStamp) "The Wallet Synced Height is $b" -ForegroundColor Yellow
                    Write-Host (Get-TimeStamp) "The Current Tip is $a" -ForegroundColor Yellow
                    Write-Host (Get-TimeStamp) "$c Blocks are Required..." -ForegroundColor Yellow
                    Start-Sleep 10
                }
            }

            No
            {
                Write-Host (Get-TimeStamp) "ERROR: There was some missing wallet detail - Please re-run this script" -ForegroundColor Red
                Start-Sleep 30
                Exit
            }
        }
    }
    

#Check Wallet Balance
$CirrusWalletBalance = (Invoke-WebRequest -Uri http://localhost:$API/api/Wallet/balance?WalletName=$Wallet -Method Get | Select-Object -ExpandProperty content | ConvertFrom-Json | Select-Object -ExpandProperty balances | Select-Object -ExpandProperty spendableamount) / 100000000
if ( $CirrusWalletBalance -ge 500.01 )
{
    Write-Host (Get-TimeStamp) "SUCCESS: $Wallet contains a balance of over 501 CRS!" -ForegroundColor Green
}
    Else
    {
        Write-Host (Get-TimeStamp) "ERROR: $Wallet does not contain a balance of over 501 CRS! Please run again and define a wallet that contains the required amount..." -ForegroundColor Red
        Start-Sleep 30
        Exit
    }

#Gather Federation Detail
""
if ( -not ( Test-Path $sideChainDataDir\federationKey.dat ) ) 
{
    $miningDAT = Read-Host "Please Enter the full path to the federationKey.dat"
    While ( -not ( Test-Path $miningDAT ) )
    {
        Write-Host Write-Host (Get-TimeStamp) "ERROR: $miningDAT is not a valid path.. Be sure to include the filename too '\federationKey.dat'" -ForegroundColor Red
        $miningDAT = Read-Host "Please Enter the full path to the federationKey.dat"
    }
    Copy-Item $miningDAT -Destination $sideChainDataDir -Force -ErrorAction Stop
}


#Perform Registration
""
$CollateralAddress = Read-Host -Prompt "Please enter your STRAX Address that contains the required collateral amount"
""
$RegisterMasternode = Read-Host -Prompt 'Would you like to register as a Masternode? Please be aware that this will incur a 500 CRS Fee. Enter "Yes" to continue or "No" to exit the script'
While ( $RegisterMasternode -ne "Yes" -and $RegisterMasternode -ne "No" )
{
    ""
    $RegisterMasternode = Read-Host 'Enter "Yes" to continue or "No" to exit the script'
    ""
}
Switch ( $RegisterMasternode )
{
    Yes
    {
        $Body = @{}
        $Body.Add("collateralAddress",$CollateralAddress)
        $Body.Add("collateralWalletName",$CollateralWallet)
        $Body.Add("collateralWalletPassword",$CollateralWalletPassword)
        $Body.Add("walletName",$CirrusWallet)
        $Body.Add("walletPassword",$WalletPassword)
        $Body.Add("walletAccount","account 0")
        Invoke-WebRequest -Uri http://localhost:38223/api/Collateral/joinfederation -Body $Body -ContentType "application/json-patch+json" -Method Post
        Write-Host (Get-TimeStamp) "SUCCESS: Your registration was succesful!! Please follow the STRAX Sidechain Masternode Setup Guide!" -ForegroundColor Green
        Start-Sleep 30
    }

    No
    {
    }
}

            
# SIG # Begin signature block
# MIIO+wYJKoZIhvcNAQcCoIIO7DCCDugCAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUnJ0yCk4hGEemzFYYgaFYrQ1w
# UzigggxDMIIFfzCCBGegAwIBAgIQB+RAO8y2U5CYymWFgvSvNDANBgkqhkiG9w0B
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
# BAGCNwIBCzEOMAwGCisGAQQBgjcCARUwIwYJKoZIhvcNAQkEMRYEFDpD56Tlvjpz
# ekJKHuF+HeyCd+UpMA0GCSqGSIb3DQEBAQUABIIBADlDNQEDbKH6B0w0KZkmB7Am
# UXx9Ny/Tshrz62DYGW9paNf2A7DrLlGuayU8Pehz3qd6UxOCwjfNYhBnKT/xvkoa
# /fwEdMNXNIxiAbDam2RKSP1U6KfPsChw2/F4Z8j9c09E9qEu2slUcOXnSz1/IW+2
# wSai9VKn5v/yhpWp+WSpVLjAcYFNvmmo3g/mmfVbpuYpfgFCrBqcXksmBYwvoV8S
# 0KR5CUbEn8rG0nn8reO7MWjRyc+g/+qGQAwcyexgoMHJWRnFMutwFEOkj1g4MLAc
# Sk31dISIR5/N2u7qbnf0g6WFb7GFuCHRRrnmOk9jlwEbiriGny8sskyvhUpIGk4=
# SIG # End signature block
