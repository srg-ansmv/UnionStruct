#!/bin/bash

cd ./src/lib/UnionStruct.Core/bin/Release || exit 1

dotnet nuget push ./UnionStruct.0.0.2.nupkg \
    --api-key "$nuget_api_key" \
    --source https://api.nuget.org/v3/index.json