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


function Get-PollsRepositoryTip
{
    $PollsRepoHeightRequest = Invoke-WebRequest -Uri http://localhost:$API/api/Voting/polls/tip
    $PollsRepoHeight = ConvertFrom-Json $PollsRepoHeightRequest
    $LocalPollsRepoHeight = $PollsRepoHeight.tipHeightPercentage
    $LocalPollsRepoHeight
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

function Check-TimeDifference3
{
    Write-Host "Checking UTC Time Difference (unixtime.co.za)" -ForegroundColor Cyan
    $timeDifSamples = @([int16]::MaxValue,[int16]::MaxValue,[int16]::MaxValue)
    $SystemTime0 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime0 = (Invoke-WebRequest https://showcase.api.linx.twenty57.net/UnixTime/tounix?date=now -TimeoutSec 3 -ErrorAction SilentlyContinue| Select-Object -ExpandProperty content).replace('"','')
    $timeDifSamples[0] = $RemoteTime0 - $SystemTime0
    $SystemTime1 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime1 = (Invoke-WebRequest https://showcase.api.linx.twenty57.net/UnixTime/tounix?date=now -TimeoutSec 3 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty content).replace('"','')
    $timeDifSamples[1] = $RemoteTime1 - $SystemTime1
    $SystemTime2 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime2 = (Invoke-WebRequest https://showcase.api.linx.twenty57.net/UnixTime/tounix?date=now -TimeoutSec 3 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty content).replace('"','')
    $timeDifSamples[2] = $RemoteTime2 - $SystemTime2
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
    $RemoteTime0 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://unixtimestamp.com/ -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[0] = $RemoteTime0 - $SystemTime0
    $SystemTime1 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime1 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://unixtimestamp.com/ -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue| Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[1] = $RemoteTime1 - $SystemTime1
    $SystemTime2 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime2 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://unixtimestamp.com/ -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue| Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[2] = $RemoteTime2 - $SystemTime2
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

function Check-TimeDifference
{
    Write-Host "Checking UTC Time Difference (google.com)" -ForegroundColor Cyan
    $timeDifSamples = @([int16]::MaxValue,[int16]::MaxValue,[int16]::MaxValue)
    $SystemTime0 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime0 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://google.com/ -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[0] = $RemoteTime0 - $SystemTime0
    $SystemTime1 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime1 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://google.com/ -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue| Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[1] = $RemoteTime1 - $SystemTime1
    $SystemTime2 = ((New-TimeSpan -Start (Get-Date "01/01/1970") -End (Get-Date).ToUniversalTime()).TotalSeconds)
    $RemoteTime2 = (New-Timespan -Start (Get-Date "01/01/1970") -End ([datetime]::Parse((Invoke-WebRequest http://google.com/ -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue| Select-Object -ExpandProperty Headers).Date).ToUniversalTime())).TotalSeconds
    $timeDifSamples[2] = $RemoteTime2 - $SystemTime2
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

if ( $NodeType -eq "50K" ) 
{    
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
    $StartNode = Start-Process 'geth.exe' -ArgumentList "--syncmode snap --http --http.corsdomain=* --http.api web3,eth,debug,personal,net --datadir=$ethDataDir" -PassThru

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
        $StartNode = Start-Process dotnet -ArgumentList "run -c Release -- -sidechain -apiport=$sideChainAPIPort -counterchainapiport=$mainChainAPIPort -redeemscript=""$redeemscript"" -publickey=$multiSigPublicKey -federationips=$federationIPs -eth_interopenabled=1 -ethereumgaspricetracking -pricetracking -eth_account=$ethAddress -eth_passphrase=$ethPassword -eth_multisigwalletcontractaddress=$ethMultiSigContract -eth_wrappedstraxcontractaddress=$ethWrappedStraxContract -eth_keyvaluestorecontractaddress=$ethKeyValueStoreContractAddress -eth_gasprice=$ethGasPrice -eth_gas=$ethGasLimit -cirrusmultisigcontractaddress=$cirrusMultiSigContract -cirrussmartcontractactiveaddress=$miningWalletAddress -eth_watcherc20=$Token1 -eth_watcherc20=$Token2 -eth_watcherc20=$Token3 -eth_watcherc20=$Token4 -eth_watcherc20=$Token5 -eth_watcherc20=$Token6" -PassThru    }
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

#Wait for Polls Repo Rebuild
$pollsTip = Get-PollsRepositoryTip
While ( $pollsTip -ne 100 )
{
    Write-Host (Get-TimeStamp) "Upgrading the Poll Store: $pollsTip %" -ForegroundColor Yellow
    Start-Sleep 10
    $pollsTip = Get-PollsRepositoryTip
}

#Wait for Peers
While ( ( Get-MaxHeight ) -eq $null ) 
{
Write-Host (Get-TimeStamp) "Waiting for Peers..." -ForegroundColor Yellow
Start-Sleep 10
}

#Wait for IBD
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
                $miningPassword = ( Read-Host "Please Enter the Passphrase used to Protect the Mining Wallet" )
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
	#Initialize InterFlux
    Write-Host (Get-TimeStamp) "Initializing InterFlux" -ForegroundColor Cyan 
    
    if ( $miningWalletPassword.GetType().Name -eq "SecureString" )
    {
        $requstBody = ConvertTo-Json @{

            name = $miningWalletName
            password = ConvertFrom-SecureString -SecureString $miningWalletPassword -AsPlainText
        }
    }
        Else {
            $requstBody = ConvertTo-Json @{

            name = $miningWalletName
            password = $miningWalletPassword
        }
    }
            
    Invoke-RestMethod -Uri http://localhost:37223/api/Wallet/load -Method Post -Body $requstBody -ContentType "application/json-patch+json" -UseBasicParsing -ErrorVariable loadWallet

    if ( $loadWallet )
    {
        Write-Host (Get-TimeStamp) "ERROR: Wallet could not be loaded. Please launch again" -ForegroundColor Red
        Shutdown-SidechainNode
        Shutdown-MainchainNode
        Start-Sleep 30
        Exit
    }

    $requstBody = ConvertTo-Json @{

        walletName = $miningWalletName
        walletPassword = $miningWalletPassword
        accountName = "account 0"
    }

    $initializeInterFlux = Invoke-RestMethod -Uri http://localhost:$API/api/Interop/initializeinterflux -Method Post -Body $requstBody -ContentType "application/json-patch+json"
    if ( $initializeInterFlux -ne $true )
    {
        Write-Host (Get-TimeStamp) "ERROR: Something went wrong. Cannot Initialize InterFlux! Please contact support in Discord" -ForegroundColor Red
        Start-Sleep 30
        Exit
    }
	
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

Set-Location $stratisMasternodeDashboardCloneDir\src\StratisMasternodeDashboard
Write-Host (Get-TimeStamp) "Starting Stratis Masternode Dashboard" -ForegroundColor Cyan
$Clean = Start-Process dotnet.exe -ArgumentList "clean" -PassThru
While ( $Clean.HasExited -ne $true ) 
{
    Write-Host (Get-TimeStamp) "Cleaning Stratis Masternode Dashboard..." -ForegroundColor Yellow
    Start-Sleep 3
}
Start-Process dotnet.exe -ArgumentList "run -c Release --nodetype 10K --mainchainport $mainChainAPIPort --sidechainport $sideChainAPIPort --env mainnet --sdadaocontractaddress CbtYboKjnk7rhNbEFzn94UZikde36h6TCb" -WindowStyle Hidden

While ( -not ( Test-Connection -TargetName 127.0.0.1 -TCPPort 37000 -ErrorAction SilentlyContinue ) )
{
    Write-Host (Get-TimeStamp) "Waiting for Stratis Masternode Dashboard..." -ForegroundColor Yellow
    Start-Sleep 3
}

Start-Process http://localhost:37000
Write-Host (Get-TimeStamp) "SUCCESS: Stratis Masternode Dashboard launched" -ForegroundColor Green


Exit


# SIG # Begin signature block
# MIIO+gYJKoZIhvcNAQcCoIIO6zCCDucCAQExCzAJBgUrDgMCGgUAMGkGCisGAQQB
# gjcCAQSgWzBZMDQGCisGAQQBgjcCAR4wJgIDAQAABBAfzDtgWUsITrck0sYpfvNR
# AgEAAgEAAgEAAgEAAgEAMCEwCQYFKw4DAhoFAAQU1EaWDGpoaK7k4e2zBPDkuV6j
# 5umgggxCMIIFfjCCBGagAwIBAgIQCrk836uc/wPyOiuycqPb5zANBgkqhkiG9w0B
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
# AYI3AgELMQ4wDAYKKwYBBAGCNwIBFTAjBgkqhkiG9w0BCQQxFgQUNiysCpBBfJBx
# J3Q7A9EnEyJLf+gwDQYJKoZIhvcNAQEBBQAEggEAGyvP7/B5JFIE9ZGjt16kSDWR
# IkdKs67US8UnYuCp/bqfUNKmeQ3P0Toe3IRqYA+TKRD9HEiy9bIzKEU1gE5S0qqh
# f3aA/yPUOyqMfjAtcq0jDig4WDNbBSrSqRh9tdfNzG7wyRDyQm9QYwgjyv8CeN7G
# 2cSyK530SjMWWgXWMGBCbqpMOmlFvgrTwigeEGkF/ORF9f00hOeII0eNq6ajivfy
# 5MlS8Du1D/MMA7lXWdx/LeIUE+QCU/V5pPergXiIp38XVTFBeae1yYLtxhgMnAlp
# v7Y6N7Rj/KgDdspTeAIKqsb6MVJ1fu3UzSYV8eT2vDTxJuOBIxHzjDEOXNAQfw==
# SIG # End signature block
