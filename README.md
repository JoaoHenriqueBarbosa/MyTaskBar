## build

```
# Debug build (com logs)
dotnet publish -c Debug -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

# Release build (sem logs)
dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DefineConstants=RELEASE
```
