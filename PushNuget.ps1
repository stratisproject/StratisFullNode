
# BASE PROJECTS
rm "src\NBitcoin\bin\Release\" -Recurse -Force
dotnet pack src\NBitcoin --configuration Release --include-source --include-symbols 
dotnet nuget push "src\NBitcoin\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin\bin\release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin --configuration Release --include-source --include-symbols 
dotnet nuget push "src\Stratis.Bitcoin\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

#FEATURES PROJECTS
rm "src\Stratis.Bitcoin.Features.Api\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.Api --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Bitcoin.Features.Api\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.BlockStore\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.BlockStore --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.BlockStore\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.Consensus\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.Consensus --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.Consensus\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.Dns\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.Dns --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.Dns\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.LightWallet\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.LightWallet --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.LightWallet\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.MemoryPool\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.MemoryPool --configuration Release --include-source --include-symbols 
dotnet nuget push "src\Stratis.Bitcoin.Features.MemoryPool\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.Miner\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.Miner --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.Miner\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.PoA\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.PoA --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.PoA\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.Notifications\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.Notifications --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.Notifications\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.RPC\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.RPC --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Bitcoin.Features.RPC\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.SmartContracts\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.SmartContracts --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.SmartContracts\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.Wallet\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.Wallet --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.Wallet\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.WatchOnlyWallet\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.WatchOnlyWallet --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Bitcoin.Features.WatchOnlyWallet\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Networks\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Networks --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Bitcoin.Networks\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Features.Collateral\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Features.Collateral --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Features.Collateral\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Features.FederatedPeg\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Features.FederatedPeg --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Features.FederatedPeg\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Features.SQLiteWalletRepository\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Features.SQLiteWalletRepository --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Features.SQLiteWalletRepository\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.ColdStaking\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.ColdStaking --configuration Release --include-source --include-symbols  
dotnet nuget push "src\Stratis.Bitcoin.Features.ColdStaking\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

# TEST PROJECTS

rm "src\Stratis.Bitcoin.Tests.Common\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Tests.Common --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Bitcoin.Tests.Common\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.IntegrationTests.Common\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.IntegrationTests.Common --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Bitcoin.IntegrationTests.Common\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

rm "src\Stratis.Bitcoin.Features.PoA.IntegrationTests.Common\bin\Release\" -Recurse -Force
dotnet pack src\Stratis.Bitcoin.Features.PoA.IntegrationTests.Common --configuration Release --include-source --include-symbols
dotnet nuget push "src\Stratis.Bitcoin.Features.PoA.IntegrationTests.Common\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

# TOOLS PROJECTS

rm "src\FodyNlogAdapter\bin\Release\" -Recurse -Force
dotnet pack src\FodyNlogAdapter --configuration Release --include-source --include-symbols 
dotnet nuget push "src\FodyNlogAdapter\bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"

PAUSE