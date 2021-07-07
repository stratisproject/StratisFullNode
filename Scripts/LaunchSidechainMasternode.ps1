#Create Functions
function Get-TimeStamp 
{
    return "[{0:dd/MM/yy} {0:HH:mm:ss}]" -f (Get-Date)
}

function Get-IndexerStatus
{
    $Headers = @{}
    $Headers.Add("Accept","application/json")
    $AsyncLoopStats = Invoke-WebRequest -Uri http://localhost:$mainChainAPIPort/api/Dashboard/AsyncLoopsStats | Select-Object -ExpandProperty content 
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
                Remove-Item -Path $mainChainDataDir\addressindex.litedb -Force
                if ( -not ( Get-Item -Path $mainChainDataDir\addressindex.litedb ) )
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

function Shutdown-Dashboard
{
    Write-Host "Shutting down Stratis Masternode Dashboard..." -ForegroundColor Yellow
    Start-Job -ScriptBlock { Invoke-WebRequest -Uri http://localhost:37000/shutdown } | Out-Null
        While ( Test-Connection -TargetName 127.0.0.1 -TCPPort 37000 -ErrorAction SilentlyContinue )
        {
            Write-Host "Waiting for Stratis Masternode Dashboard to shut down" -ForegroundColor Yellow
            Start-Sleep 5
        }
}

function Get-Median($numberSeries)
{
    $sortedNumbers = @($numberSeries | Sort-Object)
    if ( $numberSeries.Count % 2 ) 
	{
	    # Odd, pick the middle
        $sortedNumbers[(($sortedNumbers.Count - 1) / 2)]
    } 
		Else 
		{
			# Even, average the middle two
			($sortedNumbers[($sortedNumbers.Count / 2)] + $sortedNumbers[($sortedNumbers.Count / 2) - 1]) / 2
		}
}

function Check-TimeDifference
{
    Write-Host "Checking UTC Time Difference (unixtime.co.za)" -ForegroundColor Cyan
    $timeDifSamples = @([int16]::MaxValue,[int16]::MaxValue,[int16]::MaxValue)
    $SystemTime0 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime0 = (Invoke-WebRequest https://showcase.api.linx.twenty57.net/UnixTime/tounix?date=now -ErrorAction SilentlyContinue| Select-Object -ExpandProperty content)
    $timeDifSamples[0] = $RemoteTime1 - $SystemTime1
    $SystemTime1 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime1 = (Invoke-WebRequest https://showcase.api.linx.twenty57.net/UnixTime/tounix?date=now -ErrorAction SilentlyContinue | Select-Object -ExpandProperty content)
    $timeDifSamples[1] = $RemoteTime1 - $SystemTime1
    $SystemTime2 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime2 = (Invoke-WebRequest https://showcase.api.linx.twenty57.net/UnixTime/tounix?date=now -ErrorAction SilentlyContinue | Select-Object -ExpandProperty content)
    $timeDifSamples[2] = $RemoteTime1 - $SystemTime1
    $timeDif = Get-Median -numberSeries $timeDifSamples

    if ( $timeDif -gt 2 -or $timeDif -lt 2 )
    {
        Clear-Variable timeDif,timeDifSamples
        Check-TimeDifference2
    }
        Else
        {
            Write-Host "SUCCESS: Time difference is $timeDif seconds" -ForegroundColor Green
            Write-Host ""
        }
}

function Check-TimeDifference2
{
    Write-Host "Checking UTC Time Difference (unixtimestamp.com)" -ForegroundColor Cyan
    $timeDifSamples = @([int16]::MaxValue,[int16]::MaxValue,[int16]::MaxValue)
    $SystemTime0 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime0 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://unixtimestamp.com/ -UseBasicParsing -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[0] = $RemoteTime1 - $SystemTime1
    $SystemTime1 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime1 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://unixtimestamp.com/ -UseBasicParsing -ErrorAction SilentlyContinue| Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[1] = $RemoteTime1 - $SystemTime1
    $SystemTime2 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime2 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://unixtimestamp.com/ -UseBasicParsing -ErrorAction SilentlyContinue| Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[2] = $RemoteTime1 - $SystemTime1
    $timeDif = Get-Median -numberSeries $timeDifSamples

    if ( $timeDif -gt 2 -or $timeDif -lt -2 -or $timeDif -eq $null)
    {
        Clear-Variable timeDif,timeDifSamples
        Check-TimeDifference3
    }
        Else
        {
            Write-Host "SUCCESS: Time difference is $timeDif seconds" -ForegroundColor Green
            Write-Host ""
        }
}

function Check-TimeDifference3
{
    Write-Host "Checking UTC Time Difference (google.com)" -ForegroundColor Cyan
    $timeDifSamples = @([int16]::MaxValue,[int16]::MaxValue,[int16]::MaxValue)
    $SystemTime0 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime0 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://google.com/ -UseBasicParsing -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[0] = $RemoteTime1 - $SystemTime1
    $SystemTime1 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime1 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://google.com/ -UseBasicParsing -ErrorAction SilentlyContinue| Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[1] = $RemoteTime1 - $SystemTime1
    $SystemTime2 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime2 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://google.com/ -UseBasicParsing -ErrorAction SilentlyContinue| Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[2] = $RemoteTime1 - $SystemTime1
    $timeDif = Get-Median -numberSeries $timeDifSamples

    if ( $timeDif -gt 2 -or $timeDif -lt -2 -or $timeDif -eq $null)
    {
        Write-Host "ERROR: System Time is not accurate. Currently $timeDif seconds diffence with actual time! Correct Time & Date and restart" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }
        Else
        {
            Write-Host "SUCCESS: Time difference is $timeDif seconds" -ForegroundColor Green
            Write-Host ""
        }
}

#Create DataDir(s)
if ( -not ( Get-Item -Path $mainChainDataDir -ErrorAction SilentlyContinue ) )
{
    New-Item -ItemType Directory -Path $mainChainDataDir
}

if ( -not ( Get-Item -Path $sideChainDataDir -ErrorAction SilentlyContinue ) )
{
    New-Item -ItemType Directory -Path $sideChainDataDir
}

#Gather Federation Detail
if ( -not ( Test-Path $sideChainDataDir\federationKey.dat ) ) 
{
    $miningDAT = Read-Host "Please Enter the full path to the federationKey.dat"
    Copy-Item $miningDAT -Destination $sideChainDataDir -Force -ErrorAction Stop
}

#Establish Node Type
if ( $multiSigMnemonic -ne $null )
{
    $NodeType = "50K"
}

#Check for Completion
$varError = $false

if ( $NodeType -eq "50K" )
{
    if ( -not ( $multiSigPassword ) ) 
	{ 
		$varError = $true 
	}
}
	ElseIf ( -not ( $miningPassword ) ) 
	{ 
		$varError = $true 
	}

if ( -not ( $mainChainDataDir )  ) { $varError = $true }
if ( -not ( $sideChainDataDir )  ) { $varError = $true }
if ( -not ( Test-Path $sideChainDataDir/federationKey.dat ) ) { $varError = $true }
if ( $varError -eq $true )  
{
    Write-Host (Get-TimeStamp) "ERROR: Some Values were not set. Please re-run this script" -ForegroundColor Red
    Start-Sleep 30
    Exit
}

#Clear Host
Clear-Host

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

Write-Host (Get-TimeStamp) "Checking for running GETH Node" -ForegroundColor Cyan
if ( Test-Connection -TargetName 127.0.0.1 -TCPPort $gethAPIPort )
{
    Write-Host (Get-TimeStamp) "WARNING: A node is already running, please gracefully close GETH with CTRL+C to avoid forceful shutdown" -ForegroundColor DarkYellow
    ""
    While ( $shutdownCounter -le "30" )
    {
        if ( Get-Process -Name geth -ErrorAction SilentlyContinue )
        {
            Start-Sleep 3
            Write-Host (Get-TimeStamp) "Waiting for graceful shutdown ( CTRL+C )..."
            $shutdownCounter++
        }
            Else
            {
                $shutdownCounter = "31"
            }
    }
    if ( Get-Process -Name geth -ErrorAction SilentlyContinue )
    {
        Write-Host (Get-TimeStamp) "WARNING: A node is still running, performing a forced shutdown" -ForegroundColor DarkYellow
        Stop-Process -ProcessName geth -Force -ErrorAction SilentlyContinue
    }
}

#Check for running dashboard
Write-Host (Get-TimeStamp) "Checking for the Stratis Masternode Dashboard" -ForegroundColor Cyan
if ( Test-Connection -TargetName 127.0.0.1 -TCPPort 37000 -ErrorAction SilentlyContinue )
{
    Write-Host (Get-TimeStamp) "WARNING: The Stratis Masternode Dashboard is already running, will perform a graceful shutdown" -ForegroundColor DarkYellow
    ""
    Shutdown-Dashboard
}

""

#Check Time Difference
Check-TimeDifference

if ( $NodeType -eq "50K" ) 
{

    #Getting ETH Account
    $gethProcess = Start-Process geth -ArgumentList "account list --datadir=$ethDataDir" -NoNewWindow -PassThru -Wait -RedirectStandardOutput $env:TEMP\accountlist.txt 
    $gethAccountsOuput = Get-Content $env:TEMP\accountlist.txt
    $ethAddress = ($gethAccountsOuput.Split('{').Split('}') | Select-Object -Index 1).Insert('0','0x')
    Write-Host (Get-TimeStamp) "Loaded $ethAddress..." -ForegroundColor Green
    ""
    Start-Sleep 10

    #Launching GETH
    $API = $gethAPIPort
    Write-Host (Get-TimeStamp) "Starting GETH Masternode" -ForegroundColor Cyan
    $StartNode = Start-Process 'geth.exe' -ArgumentList "--syncmode fast --rpc --rpccorsdomain=* --rpcapi web3,eth,debug,personal,net --datadir=$ethDataDir" -PassThru

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
    <#
    $gethPeerCountBody = ConvertTo-Json -Compress @{
        jsonrpc = "2.0"
        method = "net_peerCount"
        id = "1"
    }

    [uint32]$gethPeerCount = Invoke-RestMethod -Uri "http://127.0.0.1:$API" -Method Post -Body $gethPeerCountBody -ContentType application/json | Select-Object -ExpandProperty result
    While ( $gethPeerCount -lt 1 )
    {
        Write-Host (Get-TimeStamp) "Waiting for Peers..." -ForegroundColor Yellow
        Start-Sleep 2
        [uint32]$gethPeerCount = Invoke-RestMethod -Uri "http://127.0.0.1:$API" -Method Post -Body $gethPeerCountBody -ContentType application/json | Select-Object -ExpandProperty result
    }

    $gethSyncStateBody = ConvertTo-Json -Compress @{
        jsonrpc = "2.0"
        method = "eth_syncing"
        id = "1"
    }

    $syncStatus = Invoke-RestMethod -Uri "http://127.0.0.1:$API" -Method Post -Body $gethSyncStateBody -ContentType application/json -ErrorAction SilentlyContinue | Select-Object -ExpandProperty result
    While ( $syncStatus -eq $false -or $syncStatus.currentBlock -eq $null )
    {
        Write-Host (Get-TimeStamp) "Waiting for Blockchain Synchronization to begin" -ForegroundColor Yellow
        $syncStatus = Invoke-RestMethod -Uri "http://127.0.0.1:$API" -Method Post -Body $gethSyncStateBody -ContentType application/json -ErrorAction SilentlyContinue | Select-Object -ExpandProperty result
        Start-Sleep 2
    }

    [uint32]$currentBlock = Invoke-RestMethod -Uri "http://127.0.0.1:$API" -Method Post -Body $gethSyncStateBody -ContentType application/json | Select-Object -ExpandProperty result | Select-Object -ExpandProperty currentBlock
    [uint32]$highestBlock = Invoke-RestMethod -Uri "http://127.0.0.1:$API" -Method Post -Body $gethSyncStateBody -ContentType application/json | Select-Object -ExpandProperty result | Select-Object -ExpandProperty highestBlock

    While ( ( $highestBlock ) -gt ( $currentBlock ) ) 
    {
        [uint32]$currentBlock = Invoke-RestMethod -Uri "http://127.0.0.1:$API" -Method Post -Body $gethSyncStateBody -ContentType application/json | Select-Object -ExpandProperty result | Select-Object -ExpandProperty currentBlock
        [uint32]$highestBlock = Invoke-RestMethod -Uri "http://127.0.0.1:$API" -Method Post -Body $gethSyncStateBody -ContentType application/json | Select-Object -ExpandProperty result | Select-Object -ExpandProperty highestBlock
        $syncProgress = $highestBlock - $currentBlock
        ""
        Write-Host (Get-TimeStamp) "The Local Height is $currentBlock" -ForegroundColor Yellow
        Write-Host (Get-TimeStamp) "The Current Tip is $highestBlock" -ForegroundColor Yellow
        Write-Host (Get-TimeStamp) "$syncProgress Blocks Require Indexing..." -ForegroundColor Yellow
        Start-Sleep 10
    }
    #>
   
    #Move to CirrusPegD
    Set-Location -Path $cloneDir/src/Stratis.CirrusPegD
}
    Else
    {
        #Move to CirrusMinerD
        Set-Location -Path $cloneDir/src/Stratis.CirrusMinerD
    }

#Start Mainchain Node
$API = $mainChainAPIPort
Write-Host (Get-TimeStamp) "Starting Mainchain Masternode" -ForegroundColor Cyan
if ( $NodeType -eq "50K" ) 
{
    $StartNode = Start-Process dotnet -ArgumentList "run -c Release -- -mainchain -addressindex=1 -apiport=$mainChainAPIPort -counterchainapiport=$sideChainAPIPort -redeemscript=""$redeemscript"" -publickey=$multiSigPublicKey -federationips=$federationIPs" -PassThru
}
    Else
    {
        $StartNode = Start-Process dotnet -ArgumentList "run -c Release -- -mainchain -addressindex=1 -apiport=$mainChainAPIPort" -PassThru
    }

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
if ( $NodeType -eq "50K" )
{
    if ( $ethGasPrice )
    {
        $StartNode = Start-Process dotnet -ArgumentList "run -c Release -- -sidechain -apiport=$sideChainAPIPort -counterchainapiport=$mainChainAPIPort -redeemscript=""$redeemscript"" -publickey=$multiSigPublicKey -federationips=$federationIPs -eth_interopenabled=1 -eth_account=$ethAddress -eth_passphrase=$ethPassword -eth_multisigwalletcontractaddress=$ethMultiSigContract -eth_wrappedstraxcontractaddress=$ethWrappedStraxContract -eth_gasprice=$ethGasPrice -eth_gas=$ethGasLimit" -PassThru
    }
        Else
        {
            $StartNode = Start-Process dotnet -ArgumentList "run -c Release -- -sidechain -apiport=$sideChainAPIPort -counterchainapiport=$mainChainAPIPort -redeemscript=""$redeemscript"" -publickey=$multiSigPublicKey -federationips=$federationIPs -eth_interopenabled=1 -eth_account=$ethAddress -eth_passphrase=$ethPassword -eth_multisigwalletcontractaddress=$ethMultiSigContract -eth_wrappedstraxcontractaddress=$ethWrappedStraxContract" -PassThru
        }
}
    Else
    {
        $StartNode = Start-Process dotnet -ArgumentList "run -c Release -- -sidechain -apiport=$sideChainAPIPort -counterchainapiport=$mainChainAPIPort" -PassThru
    }

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

#Mining Wallet Creation

$WalletNames = Invoke-WebRequest -Uri http://localhost:$sideChainAPIPort/api/Wallet/list-wallets -UseBasicParsing | Select-Object -ExpandProperty content | ConvertFrom-Json | Select-Object -ExpandProperty walletNames
if ( $WalletNames -eq $null )
{
    $CirrusMiningWallet = Read-Host "Please enter your Cirrus Mining Wallet name"
    Clear-Host

    $WalletNames = Invoke-WebRequest -Uri http://localhost:$sideChainAPIPort/api/Wallet/list-wallets -UseBasicParsing | Select-Object -ExpandProperty content | ConvertFrom-Json | Select-Object -ExpandProperty walletNames
    if ( -not ( $WalletNames -contains $CirrusMiningWallet ) ) 
    {
        Write-Host (Get-TimeStamp) "Creating Mining Wallet" -ForegroundColor Cyan
        $Body = @{} 
        $Body.Add("name", $CirrusMiningWallet)

        if ( $NodeType -eq "50K" )
        {    
            $Body.Add("password",$multiSigPassword)
            $Body.Add("passphrase",$multiSigPassword)
            $Body.Add("mnemonic",$multiSigMnemonic)
            $Body = $Body | ConvertTo-Json
            Invoke-WebRequest -Uri http://localhost:$sideChainAPIPort/api/Wallet/create -Method Post -Body $Body -ContentType "application/json" | Out-Null
        }
            Else
            {
                $Body.Add("password",$miningPassword)
                $Body.Add("passphrase",$miningPassword)
                $Body = $Body | ConvertTo-Json
                $CreateWallet = Invoke-WebRequest -Uri http://localhost:$sideChainAPIPort/api/Wallet/create -Method Post -Body $Body -ContentType "application/json"
                $Mnemonic = ($CreateWallet.Content).Trim('"')
                Write-Host (Get-TimeStamp) INFO: A Mining Wallet has now been created, please take a note of the below recovery words -ForegroundColor Yellow
                ""
                $Mnemonic
                ""
                Write-Host (Get-TimeStamp) INFO: Please take note of these words as they will be required to restore your wallet in the event of data loss -ForegroundColor Cyan

                $ReadyToContinue = Read-Host -Prompt "Have you written down your words? Enter 'Yes' to continue or 'No' to exit the script"
                While ( $ReadyToContinue -ne "Yes" -and $ReadyToContinue -ne "No" )
                {
                    ""
                    $ReadyToContinue = Read-Host -Prompt "Have you written down your words? Enter 'Yes' to continue or 'No' to exit the script"
                    ""
                }
                Switch ( $ReadyToContinue )
                {
                    Yes 
                    {
                        Write-Host (Get-TimeStamp) "INFO: Please take note of these words as they will be required to restore your wallet in the event of data loss" -ForegroundColor Green
                    }
                
                    No 
                    { 
                        Write-Host (Get-TimeStamp) "WARNING: You have said No.. In the event of data loss your wallet will be unrecoverable unless you have taken a backup of the wallet database" -ForegroundColor Red
                        Start-Sleep 60
                        Exit
                    }
                }
            }
    }
}
if ( $NodeType -eq "50K" )
{
    #Enable Federation
    Write-Host (Get-TimeStamp) "Enabling Federation" -ForegroundColor Cyan

    $Body = @{} 
    $Body.Add("password",$multiSigPassword)
    $Body.Add("passphrase",$multiSigPassword)
    $Body.Add("mnemonic",$multiSigMnemonic)
    $Body = $Body | ConvertTo-Json

    Invoke-WebRequest -Uri http://localhost:$sideChainAPIPort/api/FederationWallet/enable-federation -Method Post -Body $Body -ContentType "application/json" | Out-Null
    Write-Host (Get-TimeStamp) "Sidechain Gateway Enabled" -ForegroundColor Cyan

    Invoke-WebRequest -Uri http://localhost:$mainChainAPIPort/api/FederationWallet/enable-federation -Method Post -Body $Body -ContentType "application/json" | Out-Null
    Write-Host (Get-TimeStamp) "Mainchain Gateway Enabled" -ForegroundColor Cyan

    #Checking Mainchain Federation Status
    $MainchainFedInfo = Invoke-WebRequest -Uri http://localhost:$mainChainAPIPort/api/FederationGateway/info | Select-Object -ExpandProperty Content | ConvertFrom-Json
    if ( $MainchainFedInfo.active -ne "True" )
    {
        Write-Host (Get-TimeStamp) "ERROR: Something went wrong. Federation Inactive! Please contact support in Discord" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }

    #Checking Sidechain Federation Status
    $SidechainFedInfo = Invoke-WebRequest -Uri http://localhost:$sideChainAPIPort/api/FederationGateway/info | Select-Object -ExpandProperty Content | ConvertFrom-Json
    if ( $SidechainFedInfo.active -ne "True" )
    {
        Write-Host (Get-TimeStamp) "ERROR: Federation Inactive, ensure correct mnemonic words were entered. Please contact support in Discord" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }
}

#Checking Node Ports
if ( ( Test-Connection -TargetName 127.0.0.1 -TCPPort $mainChainAPIPort ) -and ( Test-Connection -TargetName 127.0.0.1 -TCPPort $sideChainAPIPort ) )
{
    Write-Host (Get-TimeStamp) "SUCCESS: Masternode is running" -ForegroundColor Green
    Start-Sleep 10
}
    Else
    {
        Write-Host (Get-TimeStamp) "ERROR: Cannot connect to nodes! Please contact support in Discord" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }

""
#Launching Masternode Dashboard
<#
Set-Location $stratisMasternodeDashboardCloneDir
if ( $NodeType -eq "50K" )
{
    Write-Host (Get-TimeStamp) "Starting Stratis Masternode Dashboard (50K Mode)" -ForegroundColor Cyan
    $Clean = Start-Process dotnet.exe -ArgumentList "clean" -PassThru
    While ( $Clean.HasExited -ne $true )  
    {
        Write-Host (Get-TimeStamp) "Cleaning Stratis Masternode Dashboard..." -ForegroundColor Yellow
        Start-Sleep 3
    }
    Start-Process dotnet.exe -ArgumentList "run -c Release -- --nodetype 50K --mainchainport $mainChainAPIPort --sidechainport $sideChainAPIPort --env mainnet" -WindowStyle Hidden
}
    Else
    {
        Write-Host (Get-TimeStamp) "Starting Stratis Masternode Dashboard (10K Mode)" -ForegroundColor Cyan
        $Clean = Start-Process dotnet.exe -ArgumentList "clean" -PassThru
        While ( $Clean.HasExited -ne $true ) 
        {
            Write-Host (Get-TimeStamp) "Cleaning Stratis Masternode Dashboard..." -ForegroundColor Yellow
            Start-Sleep 3
        }
        Start-Process dotnet.exe -ArgumentList "run -c Release --nodetype 10K --mainchainport $mainChainAPIPort --sidechainport $sideChainAPIPort --env mainnet" -WindowStyle Hidden
    }

While ( -not ( Test-Connection -TargetName 127.0.0.1 -TCPPort 37000 -ErrorAction SilentlyContinue ) )
{
    Write-Host (Get-TimeStamp) "Waiting for Stratis Masternode Dashboard..." -ForegroundColor Yellow
    Start-Sleep 3
}

Start-Process http://localhost:37000
Write-Host (Get-TimeStamp) "SUCCESS: Stratis Masternode Dashboard launched" -ForegroundColor Green
#>

Exit


# SIG # Begin signature block
# MIIO+wYJKoZIhvcNAQcCoIIO7DCCDugCAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQUygfSbIIh67aa4zS8Liyk87Z1
# MvqgggxDMIIFfzCCBGegAwIBAgIQB+RAO8y2U5CYymWFgvSvNDANBgkqhkiG9w0B
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
# BAGCNwIBCzEOMAwGCisGAQQBgjcCARUwIwYJKoZIhvcNAQkEMRYEFApMBalGh7Bq
# 6Lzaql1r51+Tu81XMA0GCSqGSIb3DQEBAQUABIIBAK0Xb6xT8DDw47Pzl/WFLyft
# O22nOg6uaF9i5M/Iz23GBQ9dqdTza++l6IInX0ivQBpSdb4/MlO0Gn0878pVQZKY
# NZIc911dAedpJxVOs4NzaaxxRDnYyQtNscCuTc0cEx0OkY63JPsVyYBKC9WXn5OU
# 71kOvZ8E/7kGE/LwJPHCrcZcgiYI6QXelGOVWjlWF9fu9/ULrkPemZ8QtGXxAH31
# NqZGgouKR2gOBzuMDvyJ8B09RoNUVgmzOlXS5P6DBpubUQxA+P8RQ5XoBxT+f5tR
# iiMIgklcXa0j3Oi4ZyWNCkjrHWj101uf2MHYFXNzWsQMTIz2ML5X4HZRDA354sE=
# SIG # End signature block
