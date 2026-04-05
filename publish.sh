cd .\src\lib\UnionStruct.Core\bin\Release
dotnet nuget push .\UnionStruct.0.0.2.nupkg --api-key $Env:nuget_api_key --source https://api.nuget.org/v3/index.json