# A09 cleanup: drops every HccTest_perf_* and HccTest_drill_* database (only those names) and lists what is left.
$ErrorActionPreference = 'Stop'
$names = sqlcmd -S .\SQLEXPRESS -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'HccTest[_]perf[_]%' OR name LIKE 'HccTest[_]drill[_]%'"
foreach ($n in @($names | Where-Object { $_ -match '^HccTest_(perf|drill)_[A-Za-z0-9_]+$' })) {
    sqlcmd -S .\SQLEXPRESS -E -C -b -Q "ALTER DATABASE [$n] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$n];" | Out-Null
    "dropped $n"
}
"remaining databases:"
sqlcmd -S .\SQLEXPRESS -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases ORDER BY name"
