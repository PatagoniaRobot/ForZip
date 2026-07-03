dotnet publish src/ForZip.GUI/ForZip.GUI.csproj -c Release -o Publish/GUI -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish src/ForZip.Cli/ForZip.Cli.csproj -c Release -o Publish/CLI -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
