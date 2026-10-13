using System.Collections.Generic;
using System.Reflection;
[assembly:AssemblyTitle("SpacePhone Installation Assistant")]
[assembly:AssemblyProduct("SpacePhone 3.1")]
[assembly:AssemblyVersion("3.1.1.0")]
[assembly:AssemblyFileVersion("3.1.1.0")]
namespace SpacePhoneSetup { static class Integrity {
public const string WebViewSha256="2a6add76c37bfa872eb8c2b22d45b3216b8e2a1f193e0774006340ff62ac5fa0";
public static readonly Dictionary<string,string> Engine=new Dictionary<string,string>{
{"engine/OneClick.ps1","e1ecb40294b7d79514bb620e17f48e3c94131d987f974397f7f4b826292bbbbb"},
{"engine/OneClick.Core.ps1","c6295c0dd2f038b4ce5a6edf974481354db32a7d6ca9a051bac8d70e5b32323f"},
{"engine/Legacy.Helpers.ps1","4e1ee2a7529ffb9b9206ff2c3c812f84503e6097b8037425f9a392b29e8eb755"}
};
}}
