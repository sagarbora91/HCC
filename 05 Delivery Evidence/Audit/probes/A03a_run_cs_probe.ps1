# A03a: runs probes\A03a_domain_crosscheck.cs against COPIES of the already-built Hcc.Domain.dll / Hcc.Contracts.dll in a temp folder.
# Nothing under 08 Production is built or written. Run from the repo root:  pwsh "05 Delivery Evidence/Audit/probes/A03a_run_cs_probe.ps1"
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$bin = Join-Path $root '08 Production\src\Hcc.Domain\bin\Debug\net10.0'
$work = Join-Path ([IO.Path]::GetTempPath()) 'A03a_cs_probe'
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item (Join-Path $bin 'Hcc.Domain.dll'), (Join-Path $bin 'Hcc.Contracts.dll') $work -Force
$probe = Join-Path $root '05 Delivery Evidence\Audit\probes\A03a_domain_crosscheck.cs'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><Compile Include="$probe" /><Reference Include="$work\Hcc.Domain.dll" /><Reference Include="$work\Hcc.Contracts.dll" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $work 'A03a_probe.csproj') -Encoding utf8
'<Project></Project>' | Set-Content (Join-Path $work 'Directory.Build.props') -Encoding utf8
'<Project></Project>' | Set-Content (Join-Path $work 'Directory.Packages.props') -Encoding utf8
dotnet run --project (Join-Path $work 'A03a_probe.csproj') -c Release
