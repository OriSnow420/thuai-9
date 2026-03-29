# THUAI Server

Run server:

```bash
dotnet run --project src/thuai
```

or use a shorter version:

```bash
make run
```

Run tests:

```bash
dotnet test
```

If the restoring doesn't end, it's the network that has the problem. Use a mirror by writing the following content to `~/.nuget/NuGet/NuGet.Config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="huaweicloud" value="https://mirrors.huaweicloud.com" />
  </packageSources>
</configuration>
```
