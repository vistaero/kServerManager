# C# server launcher

This project replaces the PowerShell console launcher and includes a portable JDK downloader.

## Build and publish

From the repository root:

```powershell
dotnet publish .\Launcher\kServerManager.Launcher.csproj --configuration Release --runtime win-x64 --self-contained false
```

Copy the files from `Launcher\bin\Release\net8.0\win-x64\publish` into the Minecraft server folder beside the server `.jar` files, then run `kServerManager.Launcher.exe`. The launcher and WPF manager both read and write `java_config.json` beside their executable, so place them in the same server folder to share Java, JAR, and maximum-memory settings.

This framework-dependent publish needs the .NET 8 runtime on the server computer. To bundle the runtime instead, publish with `--self-contained true`.

## Download JDKs

```powershell
kServerManager.Launcher.exe install-jdks
kServerManager.Launcher.exe install-jdks 17 21 25
kServerManager.Launcher.exe install-jdks 21 --output-directory D:\Java\toolchains
kServerManager.Launcher.exe install-jdks --jdk-versions 17 21 25
```

With no versions, the command prompts for versions; submitting an empty line downloads 8, 17, 21, and 25. Downloads come from the Eclipse Adoptium API. By default, portable JDKs are cached under `%LOCALAPPDATA%\kServerManager\jdks`.

The console menu can also download JDKs, select an installed Java executable, select a server JAR, configure maximum memory, start the server, and synchronize the server directory over `scp` to the configured Raspberry Pi host.
