dotnet publish -c Release --framework net5.0
dotnet publish -c Release --framework net6.0
dotnet publish -c Release --framework net7.0
dotnet publish -c Release --framework net8.0
dotnet publish -c Release --framework netcoreapp3.1
dotnet publish -c Release --framework netstandard2.0
dotnet pack --include-symbols --include-source