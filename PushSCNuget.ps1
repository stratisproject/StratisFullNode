$packageNames = @("Stratis.Sidechains.Networks","Stratis.SmartContracts.CLR", "Stratis.SmartContracts.CLR.Validation", "Stratis.SmartContracts.Core", "Stratis.SmartContracts.Networks","Stratis.SmartContracts.RuntimeObserver","Stratis.SmartContracts.RuntimeObserver","Stratis.SmartContracts.Tests.Common")

# A little gross to have to enter src/ and then go back after, but this is where the file is atm 
cd "src"

foreach ($packageName in $packageNames){
	cd $packageName
	rm "bin\Release\" -Recurse -Force -ErrorAction Ignore
	dotnet pack --configuration Release --include-source --include-symbols 
	dotnet nuget push "bin\Release\*.symbols.nupkg" -k "" --source "https://api.nuget.org/v3/index.json"
	cd ..
}

cd ..