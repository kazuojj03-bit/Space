# Historical source correspondence

The historical production plugin SHA256 is `a6835e5c4a5b932a8870621f4f4beba35560c29754f12e564240bea0a487b0f2` (244,224 bytes). Runtime binaries have not been rebuilt or altered in preparing this source repository.

The reviewer copy of `plugin/BrowserClient.cs` omits exactly one development-machine-specific branch. All its behavior is shown here, with only the local developer directory literal redacted:

```csharp
if (string.Equals(BepInEx.Paths.GameRootPath,
    @"<developer-local-test-copy-path>", StringComparison.OrdinalIgnoreCase))
{
    start.ArgumentList.Add("--profile");
    start.ArgumentList.Add(Path.Combine(BepInEx.Paths.GameRootPath,"browser-test-profile"));
    start.ArgumentList.Add("--test-capture-dir");
    start.ArgumentList.Add(BepInEx.Paths.GameRootPath);
}
```

It chooses an isolated test browser profile/capture folder only when the exact test-copy path matches. The matching `webhost/Program.cs` test/capture implementation is included in full. All network and process-launch code outside this one disclosed branch remains present. This is a reviewer-source privacy difference, not a claim of byte-identical binary reconstruction. The source-only change does not replace the historical release DLL.

Original source-difference notes and exact redacted diff: [PLUGIN-SOURCE-DIFFERENCES.txt](PLUGIN-SOURCE-DIFFERENCES.txt), [BrowserClient-public-source.diff](BrowserClient-public-source.diff).

Locally identified legacy full archive: `SpacePhone-3.1-Full-Installer.zip`, 50,567,356 bytes, SHA256 `1467049816c25f5523e187151c88f25eaa33d740460751c1b8fa0d200f98f889`, 316 entries. This is an archive identity, not an independently retrieved Nexus remote-file hash.

The separate OneClick Full bundle has a historical build record, 300,501,432 bytes, SHA256 `259db9e68b62083f24caa5a2f5c02072aee4a3a5db0f26695e28a035cb7f3152`. That ZIP was not present in the local outputs during the current follow-up, so this repository does not falsely state a new ZIP recheck was performed. Its separately staged source/components remain available locally; no production update is installed by source preparation.
