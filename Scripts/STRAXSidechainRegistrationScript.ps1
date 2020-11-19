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
    #Shutdown-MainchainNode
}

Write-Host (Get-TimeStamp) "Checking for running Sidechain Node" -ForegroundColor Cyan
if ( Test-Connection -TargetName 127.0.0.1 -TCPPort $sideChainAPIPort )
{
    Write-Host (Get-TimeStamp) "WARNING: A node is already running, will perform a graceful shutdown" -ForegroundColor DarkYellow
    ""
    #Shutdown-SidechainNode
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
    #if ( $StartNode.HasExited -eq $true )
    #{
    #    Write-Host (Get-TimeStamp) "ERROR: Something went wrong. Please contact support in Discord" -ForegroundColor Red
    #    Start-Sleep 30
    #    Exit
    #}
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
    #if ( $StartNode.HasExited -eq $true )
    #{
    #    Write-Host (Get-TimeStamp) "ERROR: Something went wrong. Please contact support in Discord" -ForegroundColor Red
    #    Start-Sleep 30
    #    Exit
    #}
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
""
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

    
#Perform Registration
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
        pause

    }

    No
    {
    }
}

            